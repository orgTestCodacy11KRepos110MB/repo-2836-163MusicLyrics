using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MusicLyricApp.Bean;
using MusicLyricApp.Exception;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace MusicLyricApp.Utils
{
    public static class GlobalUtils
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static string GetSongKey(string displayId, bool verbatimLyric)
        {
            return displayId + "_" + verbatimLyric;
        }

        public static string FormatDate(long millisecond)
        {
            var date = (new DateTime(1970, 1, 1))
                    .AddMilliseconds(double.Parse(millisecond.ToString()))
                    .AddHours(8) // +8 时区
                ;

            return date.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private static readonly Dictionary<SearchSourceEnum, string> SearchSourceKeywordDict =
            new Dictionary<SearchSourceEnum, string>
            {
                { SearchSourceEnum.NET_EASE_MUSIC, "163.com" },
                { SearchSourceEnum.QQ_MUSIC, "qq.com" },
            };

        private static readonly Dictionary<SearchSourceEnum, Dictionary<SearchTypeEnum, string>> SearchTypeKeywordDict =
            new Dictionary<SearchSourceEnum, Dictionary<SearchTypeEnum, string>>
            {
                {
                    SearchSourceEnum.NET_EASE_MUSIC, new Dictionary<SearchTypeEnum, string>
                    {
                        { SearchTypeEnum.SONG_ID, "song?id=" },
                        { SearchTypeEnum.ALBUM_ID, "album?id=" },
                        { SearchTypeEnum.PLAYLIST_ID, "playlist?id=" },
                    }
                },
                {
                    SearchSourceEnum.QQ_MUSIC, new Dictionary<SearchTypeEnum, string>
                    {
                        { SearchTypeEnum.SONG_ID, "songDetail/" },
                        { SearchTypeEnum.ALBUM_ID, "albumDetail/" },
                        { SearchTypeEnum.PLAYLIST_ID, "playlist/" },
                    }
                }
            };

        /// <summary>
        /// 输入参数校验
        /// </summary>
        /// <param name="input">输入参数</param>
        /// <returns></returns>
        public static string CheckInputId(string input, PersistParamBean paramBean)
        {
            // 输入参数为空
            if (string.IsNullOrEmpty(input))
            {
                throw new MusicLyricException(ErrorMsg.INPUT_ID_ILLEGAL);
            }

            // 自动识别音乐提供商
            foreach (var pair in SearchSourceKeywordDict.Where(pair => input.Contains(pair.Value)))
            {
                paramBean.SearchSource = pair.Key;
            }

            // 自动识别搜索类型
            foreach (var pair in SearchTypeKeywordDict[paramBean.SearchSource]
                         .Where(pair => input.Contains(pair.Value)))
            {
                paramBean.SearchType = pair.Key;
            }

            // 网易云，纯数字，直接通过
            if (paramBean.SearchSource == SearchSourceEnum.NET_EASE_MUSIC && CheckNum(input))
            {
                return input;
            }

            // QQ 音乐，数字+字母，直接通过
            if (paramBean.SearchSource == SearchSourceEnum.QQ_MUSIC && Regex.IsMatch(input, @"^[a-zA-Z0-9]*$"))
            {
                return input;
            }

            // URL 关键字提取
            var urlKeyword = SearchTypeKeywordDict[paramBean.SearchSource][paramBean.SearchType];
            var index = input.IndexOf(urlKeyword, StringComparison.Ordinal);
            if (index != -1)
            {
                var sb = new StringBuilder();
                foreach (var c in input.Substring(index + urlKeyword.Length).ToCharArray())
                {
                    if (char.IsLetterOrDigit(c))
                    {
                        sb.Append(c);
                    }
                    else
                    {
                        break;
                    }
                }

                return sb.ToString();
            }

            // QQ 音乐，歌曲短链接
            if (paramBean.SearchSource == SearchSourceEnum.QQ_MUSIC && input.Contains("fcgi-bin/u"))
            {
                const string keyword = "window.__ssrFirstPageData__";
                var html = HttpUtils.HttpGet(input);

                var indexOf = html.IndexOf(keyword);

                if (indexOf != -1)
                {
                    var endIndexOf = html.IndexOf("</script>", indexOf);
                    if (endIndexOf != -1)
                    {
                        var data = html.Substring(indexOf + keyword.Length, endIndexOf - indexOf - keyword.Length);

                        data = data.Trim().Substring(1);

                        var obj = (JObject)JsonConvert.DeserializeObject(data);

                        var songs = obj["songList"].ToObject<QQMusicBean.Song[]>();

                        if (songs.Length > 0)
                        {
                            return songs[0].Id;
                        }
                    }
                }
            }

            throw new MusicLyricException(ErrorMsg.INPUT_ID_ILLEGAL);
        }

        /**
         * 检查字符串是否为数字
         */
        public static bool CheckNum(string s)
        {
            return Regex.IsMatch(s, "^\\d+$", RegexOptions.Compiled);
        }

        /**
         * 获取输出文件名
         */
        public static string GetOutputName(SongVo songVo, OutputFilenameTypeEnum typeEnum)
        {
            if (songVo == null)
            {
                var ex = new ArgumentNullException(nameof(songVo));
                Logger.Error(ex);
                throw ex;
            }

            string outputName;
            switch (typeEnum)
            {
                case OutputFilenameTypeEnum.NAME_SINGER:
                    outputName = $"{songVo.Name} - {songVo.Singer}";
                    break;
                case OutputFilenameTypeEnum.SINGER_NAME:
                    outputName = $"{songVo.Singer} - {songVo.Name}";
                    break;
                case OutputFilenameTypeEnum.NAME:
                    outputName = songVo.Name;
                    break;
                default:
                    throw new MusicLyricException(ErrorMsg.SYSTEM_ERROR);
            }

            return GetSafeFilename(outputName);
        }

        private static string GetSafeFilename(string arbitraryString)
        {
            if (arbitraryString == null)
            {
                var ex = new ArgumentNullException(nameof(arbitraryString));
                Logger.Error(ex);
                throw ex;
            }

            var invalidChars = System.IO.Path.GetInvalidFileNameChars();
            var replaceIndex = arbitraryString.IndexOfAny(invalidChars, 0);
            if (replaceIndex == -1)
                return arbitraryString;

            var r = new StringBuilder();
            var i = 0;

            do
            {
                r.Append(arbitraryString, i, replaceIndex - i);

                switch (arbitraryString[replaceIndex])
                {
                    case '"':
                        r.Append("''");
                        break;
                    case '<':
                        r.Append('\u02c2'); // '˂' (modifier letter left arrowhead)
                        break;
                    case '>':
                        r.Append('\u02c3'); // '˃' (modifier letter right arrowhead)
                        break;
                    case '|':
                        r.Append('\u2223'); // '∣' (divides)
                        break;
                    case ':':
                        r.Append('-');
                        break;
                    case '*':
                        r.Append('\u2217'); // '∗' (asterisk operator)
                        break;
                    case '\\':
                    case '/':
                        r.Append('\u2044'); // '⁄' (fraction slash)
                        break;
                    case '\0':
                    case '\f':
                    case '?':
                        break;
                    case '\t':
                    case '\n':
                    case '\r':
                    case '\v':
                        r.Append(' ');
                        break;
                    default:
                        r.Append('_');
                        break;
                }

                i = replaceIndex + 1;
                replaceIndex = arbitraryString.IndexOfAny(invalidChars, i);
            } while (replaceIndex != -1);

            r.Append(arbitraryString, i, arbitraryString.Length - i);

            return r.ToString();
        }

        public static Encoding GetEncoding(OutputEncodingEnum encodingEnum)
        {
            switch (encodingEnum)
            {
                case OutputEncodingEnum.GB_2312:
                    return Encoding.GetEncoding("GB2312");
                case OutputEncodingEnum.GBK:
                    return Encoding.GetEncoding("GBK");
                case OutputEncodingEnum.UTF_8_BOM:
                    return new UTF8Encoding(true);
                case OutputEncodingEnum.UNICODE:
                    return Encoding.Unicode;
                default:
                    // utf-8 and others
                    return new UTF8Encoding(false);
            }
        }

        public static int toInt(string str, int defaultValue)
        {
            var result = defaultValue;

            int.TryParse(str, out result);

            return result;
        }

        public static List<T> GetEnumList<T>() where T : Enum
        {
            return Enum.GetValues(typeof(T)).OfType<T>().ToList();
        }

        public static string[] GetEnumDescArray<T>() where T : Enum
        {
            var list = GetEnumList<T>();
            var result = new string[list.Count];

            for (var i = 0; i < list.Count; i++)
            {
                result[i] = list[i].ToDescription();
            }

            return result;
        }
    }
}
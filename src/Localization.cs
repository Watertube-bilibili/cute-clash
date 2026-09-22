using System;
using System.Globalization;

namespace CuteClash
{
    /// <summary>Application-owned text only. Profile names, node names and core output stay unchanged.</summary>
    public static class Localization
    {
        private static string language = "zh-CN";

        public static string Language
        {
            get { return language; }
            set { language = NormalizeLanguage(value); }
        }

        public static string NormalizeLanguage(string value)
        {
            return String.Equals(value, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh-CN";
        }

        public static string T(string chinese, string english)
        {
            return language == "en" ? english : chinese;
        }

        public static string Format(string chinese, string english, params object[] arguments)
        {
            return String.Format(language == "en" ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("zh-CN"), T(chinese, english), arguments);
        }
    }
}

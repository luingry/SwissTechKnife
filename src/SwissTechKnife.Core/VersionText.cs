using System;

namespace SwissTechKnife.Core
{
    public static class VersionText
    {
        /// <summary>Parses "1.2" / "1.2.3" / "1.2.3-beta.1" (pre-release suffix ignored for ordering).</summary>
        public static bool TryParse(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }
            string core = text.Trim();
            int dash = core.IndexOfAny(new[] { '-', '+' });
            if (dash >= 0)
            {
                core = core.Substring(0, dash);
            }
            if (!Version.TryParse(core, out Version parsed))
            {
                return false;
            }
            version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
            return true;
        }

        public static Version ParseOrZero(string text) => TryParse(text, out Version v) ? v : new Version(0, 0, 0);

        /// <summary>True when <paramref name="candidate"/> is a strictly newer version than <paramref name="current"/>.</summary>
        public static bool IsNewer(string candidate, string current) => ParseOrZero(candidate) > ParseOrZero(current);
    }
}

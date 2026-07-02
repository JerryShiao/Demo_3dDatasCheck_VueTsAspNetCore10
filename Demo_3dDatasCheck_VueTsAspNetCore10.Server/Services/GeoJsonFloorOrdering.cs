namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// GeoJSON 匯入專用樓層排序與解析規則
    /// </summary>
    internal static class GeoJsonFloorOrdering
    {
        internal enum FloorCategory
        {
            Basement = 0,
            Regular = 1,
            Unknown = 2,
            Rooftop = 3,
        }

        internal readonly record struct FloorSortKey(FloorCategory Category, int Number, string Raw);

        /// <summary>
        /// 解析樓層字串為排序鍵
        /// </summary>
        internal static FloorSortKey Parse(string? floor)
        {
            var raw = floor?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new FloorSortKey(FloorCategory.Unknown, 0, raw);
            }

            var upper = raw.ToUpperInvariant();

            if (upper.StartsWith('B') && upper.Length > 1)
            {
                var digits = ExtractDigits(upper[1..]);
                if (int.TryParse(digits, out var basementNo) && basementNo > 0)
                {
                    return new FloorSortKey(FloorCategory.Basement, basementNo, raw);
                }
            }

            if (IsRooftopLabel(upper))
            {
                var digits = ExtractDigits(raw);
                var rooftopNo = int.TryParse(digits, out var n) ? n : 0;
                return new FloorSortKey(FloorCategory.Rooftop, rooftopNo, raw);
            }

            var regularDigits = ExtractDigits(raw);
            if (int.TryParse(regularDigits, out var floorNo) && floorNo > 0)
            {
                return new FloorSortKey(FloorCategory.Regular, floorNo, raw);
            }

            return new FloorSortKey(FloorCategory.Unknown, 0, raw);
        }

        /// <summary>
        /// 樓層排序比較器
        /// </summary>
        internal static int Compare(string? a, string? b)
        {
            var keyA = Parse(a);
            var keyB = Parse(b);

            var categoryCompare = keyA.Category.CompareTo(keyB.Category);
            if (categoryCompare != 0)
            {
                return categoryCompare;
            }

            var numberCompare = keyA.Number.CompareTo(keyB.Number);
            if (numberCompare != 0)
            {
                return numberCompare;
            }

            return string.Compare(keyA.Raw, keyB.Raw, StringComparison.Ordinal);
        }

        /// <summary>
        /// 供檢核訊息顯示的樓層標籤
        /// </summary>
        internal static string GetDisplayLabel(string? floor)
        {
            var key = Parse(floor);
            if (!string.IsNullOrWhiteSpace(key.Raw))
            {
                return key.Raw;
            }

            return "?";
        }

        private static bool IsRooftopLabel(string upper)
        {
            return upper.StartsWith('R')
                || upper.Contains("RF", StringComparison.Ordinal)
                || upper.Contains("PRF", StringComparison.Ordinal)
                || upper.Contains("ROOF", StringComparison.Ordinal);
        }

        private static string ExtractDigits(string value)
        {
            return new string(value.Where(char.IsDigit).ToArray());
        }
    }
}

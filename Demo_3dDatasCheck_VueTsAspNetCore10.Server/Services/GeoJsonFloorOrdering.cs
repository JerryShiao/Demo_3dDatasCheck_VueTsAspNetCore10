namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// GeoJSON 匯入專用樓層排序與解析規則
    /// </summary>
    internal static class GeoJsonFloorOrdering
    {
        /// <summary>
        /// 樓層分類（排序優先序由小到大）
        /// </summary>
        internal enum FloorCategory
        {
            /// <summary>
            /// 地下室（B1、B2…）
            /// </summary>
            Basement = 0,

            /// <summary>
            /// 一般樓層（1、2F、3樓…）
            /// </summary>
            Regular = 1,

            /// <summary>
            /// 無法辨識的樓層標籤
            /// </summary>
            Unknown = 2,

            /// <summary>
            /// 屋頂層（R、RF、ROOF…）
            /// </summary>
            Rooftop = 3,
        }

        /// <summary>
        /// 樓層排序鍵：分類 → 數字 → 原始字串
        /// </summary>
        /// <param name="Category">樓層分類</param>
        /// <param name="Number">樓層數字（地下室/一般層/屋頂編號）</param>
        /// <param name="Raw">原始樓層字串</param>
        internal readonly record struct FloorSortKey(FloorCategory Category, int Number, string Raw);

        #region ◆解析樓層字串為排序鍵 [Parse]
        /// <summary>
        /// 解析樓層字串為排序鍵（地下室 → 一般層 → 未知 → 屋頂）
        /// </summary>
        /// <param name="floor">樓層字串</param>
        /// <returns>排序鍵</returns>
        internal static FloorSortKey Parse(string? floor)
        {
            var raw = floor?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new FloorSortKey(FloorCategory.Unknown, 0, raw);
            }

            var upper = raw.ToUpperInvariant();

            // 地下室：B 開頭且後接數字（B1、B2…）
            if (upper.StartsWith('B') && upper.Length > 1)
            {
                var digits = ExtractDigits(upper[1..]);
                if (int.TryParse(digits, out var basementNo) && basementNo > 0)
                {
                    return new FloorSortKey(FloorCategory.Basement, basementNo, raw);
                }
            }

            // 屋頂層：R / RF / PRF / ROOF 等
            if (IsRooftopLabel(upper))
            {
                var digits = ExtractDigits(raw);
                var rooftopNo = int.TryParse(digits, out var n) ? n : 0;
                return new FloorSortKey(FloorCategory.Rooftop, rooftopNo, raw);
            }

            // 一般樓層：抽取數字（1、2F、3樓…）
            var regularDigits = ExtractDigits(raw);
            if (int.TryParse(regularDigits, out var floorNo) && floorNo > 0)
            {
                return new FloorSortKey(FloorCategory.Regular, floorNo, raw);
            }

            return new FloorSortKey(FloorCategory.Unknown, 0, raw);
        }
        #endregion

        #region ◆樓層排序比較 [Compare]
        /// <summary>
        /// 樓層排序比較器：先分類，再數字，最後原始字串
        /// </summary>
        /// <param name="a">樓層字串 A</param>
        /// <param name="b">樓層字串 B</param>
        /// <returns>比較結果（負／零／正）</returns>
        internal static int Compare(string? a, string? b)
        {
            var keyA = Parse(a);
            var keyB = Parse(b);

            // 先比分類
            var categoryCompare = keyA.Category.CompareTo(keyB.Category);
            if (categoryCompare != 0)
            {
                return categoryCompare;
            }

            // 再比樓層數字
            var numberCompare = keyA.Number.CompareTo(keyB.Number);
            if (numberCompare != 0)
            {
                return numberCompare;
            }

            // 最後以原始字串穩定排序
            return string.Compare(keyA.Raw, keyB.Raw, StringComparison.Ordinal);
        }
        #endregion

        #region ◆取得顯示用樓層標籤 [GetDisplayLabel]
        /// <summary>
        /// 供檢核訊息顯示的樓層標籤；空白時回傳 "?"
        /// </summary>
        /// <param name="floor">樓層字串</param>
        /// <returns>顯示標籤</returns>
        internal static string GetDisplayLabel(string? floor)
        {
            var key = Parse(floor);
            if (!string.IsNullOrWhiteSpace(key.Raw))
            {
                return key.Raw;
            }

            return "?";
        }
        #endregion

        #region ◆樓層解析輔助 [Helpers]
        /// <summary>
        /// 判斷是否為屋頂層標籤
        /// </summary>
        private static bool IsRooftopLabel(string upper)
        {
            return upper.StartsWith('R')
                || upper.Contains("RF", StringComparison.Ordinal)
                || upper.Contains("PRF", StringComparison.Ordinal)
                || upper.Contains("ROOF", StringComparison.Ordinal);
        }

        /// <summary>
        /// 抽取字串中的數字字元
        /// </summary>
        private static string ExtractDigits(string value)
        {
            return new string(value.Where(char.IsDigit).ToArray());
        }
        #endregion
    }
}

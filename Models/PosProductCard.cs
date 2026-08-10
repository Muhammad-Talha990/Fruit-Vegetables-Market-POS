using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FruitVegetableMarketPOS.Models
{
    /// <summary>
    /// Display model for a product card on the fruit/veg POS grid.
    /// Prefers a saved product photo; otherwise uses Assets/Products images.
    /// </summary>
    public class PosProductCard : INotifyPropertyChanged
    {
        public DailyItemSelection Selection { get; set; } = new();
        public int DailySelectionId => Selection.DailySelectionId;
        public int ItemId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? NameUrdu { get; set; }
        public string DisplayTitle => Name;
        public string DisplayUrdu => NameUrdu ?? string.Empty;
        public string Unit { get; set; } = "piece";
        public int? CategoryId { get; set; }
        public double DisplayPrice { get; set; }
        public string? Barcode { get; set; }
        public string? ImagePath { get; set; }
        public string SearchText { get; set; } = string.Empty;

        private bool _isAvailable = true;
        /// <summary>False = deactivated for today (faded card, green tick to restore).</summary>
        public bool IsAvailable
        {
            get => _isAvailable;
            set
            {
                if (_isAvailable == value) return;
                _isAvailable = value;
                Selection.IsAvailable = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CardOpacity));
                OnPropertyChanged(nameof(ToggleGlyph));
                OnPropertyChanged(nameof(ToggleBrush));
                OnPropertyChanged(nameof(ToggleToolTip));
            }
        }

        public double CardOpacity => IsAvailable ? 1.0 : 0.42;
        public string ToggleGlyph => IsAvailable ? "✕" : "✓";
        public System.Windows.Media.Brush ToggleBrush => IsAvailable
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE5, 0x39, 0x35))
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0x7D, 0x32));
        public string ToggleToolTip => IsAvailable
            ? "Deactivate for today · آج غیر فعال"
            : "Activate again · دوبارہ فعال";

        /// <summary>User-facing item code (#1, #2…) for Add Today / scan.</summary>
        public string PosCode =>
            !string.IsNullOrWhiteSpace(Barcode) ? Barcode.Trim() : ItemId.ToString();

        public string CodeDisplay => $"#{PosCode}";

        public string PriceDisplay => $"Rs.{DisplayPrice:N0}";

        public string IconKey
        {
            get
            {
                var n = (Name ?? string.Empty).Trim().ToLowerInvariant();
                // Longer / specific names first so "pineapple" ≠ "apple", etc.
                if (n.Contains("pineapple")) return "pineapple";
                if (n.Contains("watermelon")) return "watermelon";
                if (n.Contains("pomegranate")) return "pomegranate";
                if (n.Contains("strawberry")) return "strawberry";
                if (n.Contains("sweet potato")) return "sweetpotato";
                if (n.Contains("spring onion")) return "springonion";
                if (n.Contains("long bottle gourd")) return "longbottlegourd";
                if (n.Contains("bottle gourd") || n.Contains("lauki")) return "bottlegourd";
                if (n.Contains("bitter gourd") || n.Contains("karela")) return "bittergourd";
                if (n.Contains("apple gourd") || n.Contains("tinda")) return "applegourd";
                if (n.Contains("java plum") || n.Contains("jamun")) return "javaplum";
                if (n.Contains("cauliflower") || n.Contains("cauli")) return "cauliflower";
                if (n.Contains("eggplant") || n.Contains("brinjal")) return "eggplant";
                if (n.Contains("coriander") || n.Contains("dhania")) return "coriander";
                if (n.Contains("fenugreek")) return "fenugreek";
                if (n.Contains("kiwi")) return "kiwi";
                if (n.Contains("broccoli")) return "broccoli";
                if (n.Contains("cucumber") || n.Contains("cocumber")) return "cucumber";
                if (n.Contains("zucchini") || n.Contains("zuchinni") || n.Contains("tori") || n.Contains("توری")) return "zucchini";
                if (n.Contains("capsicum") || n.Contains("pepper") || n.Contains("shimla")) return "pepper";
                if (n.Contains("chili") || n.Contains("chilli") || n.Contains("chillie")) return "chili";
                if (n.Contains("coconut")) return "coconut";
                if (n.Contains("papaya")) return "papaya";
                if (n.Contains("banana")) return "banana";
                if (n.Contains("mango")) return "mango";
                if (n.Contains("orange") || n.Contains("malta")) return "orange";
                if (n.Contains("grape")) return "grape";
                if (n.Contains("guava")) return "guava";
                if (n.Contains("peach")) return "peach";
                if (n.Contains("pear")) return "pear";
                if (n.Contains("lychee") || n.Contains("litchi")) return "lychee";
                if (n.Contains("cherry")) return "cherry";
                if (n.Contains("melon") && !n.Contains("water")) return "melon";
                if (n.Contains("apricot") || n.Contains("appricot")) return "apricot";
                if (n.Contains("plum") && !n.Contains("java")) return "plum";
                if (n.Contains("date")) return "dates";
                if (n.Contains("lemon") || n.Contains("lime")) return "lemon";
                if (n.Contains("apple")) return "apple";
                if (n.Contains("tomato")) return "tomato";
                if (n.Contains("potato") && !n.Contains("sweet")) return "potato";
                if (n.Contains("onion")) return "onion";
                if (n.Contains("carrot")) return "carrot";
                if (n.Contains("ginger")) return "ginger";
                if (n.Contains("garlic")) return "garlic";
                if (n.Contains("spinach")) return "spinach";
                if (n.Contains("mint")) return "mint";
                if (n.Contains("okra") || n.Contains("ladyfinger") || n.Contains("lady finger") || n.Contains("bhindi")) return "okra";
                if (n.Contains("cabbage")) return "cabbage";
                if (n.Contains("peas") || n.EndsWith("pea") || n.Contains(" pea")) return "peas";
                if (n.Contains("radish")) return "radish";
                if (n.Contains("turnip")) return "turnip";
                if (n.Contains("beet")) return "beetroot";
                if (n.Contains("pumpkin")) return "pumpkin";
                if (n.Contains("corn")) return "corn";
                if (n.Contains("lettuce")) return "lettuce";
                return "default";
            }
        }

        /// <summary>True when a real photo file is available (custom or Assets/Products).</summary>
        public bool HasPhoto => ResolvePhotoPath() != null;

        /// <summary>WPF-ready bitmap from custom or bundled Assets/Products image.</summary>
        public ImageSource? PhotoSource
        {
            get
            {
                var path = ResolvePhotoPath();
                if (path == null) return null;
                return GetOrLoadBitmap(path);
            }
        }

        private static readonly Dictionary<string, ImageSource> _bitmapCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _bitmapCacheLock = new();

        private static ImageSource? GetOrLoadBitmap(string path)
        {
            lock (_bitmapCacheLock)
            {
                if (_bitmapCache.TryGetValue(path, out var cached))
                    return cached;
            }

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.DecodePixelWidth = 200;
                bmp.EndInit();
                bmp.Freeze();

                lock (_bitmapCacheLock)
                    _bitmapCache[path] = bmp;

                return bmp;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Alias stems (normalized, letters/digits only) → match Assets/Products filenames.
        /// Covers typos in filenames (Appricot, Zuchinni, cocumber, cauli flower).
        /// </summary>
        private static readonly Dictionary<string, string[]> PhotoAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["apple"] = new[] { "apple" },
            ["banana"] = new[] { "banana" },
            ["mango"] = new[] { "mango" },
            ["orange"] = new[] { "orange" },
            ["grape"] = new[] { "grape", "grapes" },
            ["watermelon"] = new[] { "watermelon" },
            ["guava"] = new[] { "guava" },
            ["pomegranate"] = new[] { "pomegranate" },
            ["peach"] = new[] { "peach" },
            ["pear"] = new[] { "pear" },
            ["lychee"] = new[] { "lychee", "litchi" },
            ["strawberry"] = new[] { "strawberry" },
            ["lemon"] = new[] { "lemon" },
            ["cherry"] = new[] { "cherry" },
            ["tomato"] = new[] { "tomato" },
            ["potato"] = new[] { "potato" },
            ["onion"] = new[] { "onion" },
            ["carrot"] = new[] { "carrot" },
            ["ginger"] = new[] { "ginger" },
            ["garlic"] = new[] { "garlic" },
            ["spinach"] = new[] { "spinach" },
            ["mint"] = new[] { "mint" },
            ["okra"] = new[] { "ladyfinger", "okra" },
            ["cabbage"] = new[] { "cabbage" },
            ["radish"] = new[] { "radish" },
            ["turnip"] = new[] { "turnip" },
            ["beetroot"] = new[] { "beetroot", "beet" },
            ["corn"] = new[] { "corn" },
            ["lettuce"] = new[] { "lettuce" },
            ["broccoli"] = new[] { "broccoli" },
            ["cauliflower"] = new[] { "cauliflower" },
            ["springonion"] = new[] { "springonion", "onion" },
            ["fenugreek"] = new[] { "fenugreek" },
            ["kiwi"] = new[] { "kiwi" },
            ["pumpkin"] = new[] { "pumpkin" },
            ["eggplant"] = new[] { "eggplant" },
            ["coriander"] = new[] { "coriander" },
            ["cucumber"] = new[] { "cucumber", "cocumber" },
            ["chili"] = new[] { "chillie", "chilli", "chili" },
            ["pepper"] = new[] { "capsicum", "pepper" },
            ["bottlegourd"] = new[] { "bottlegourd" },
            ["longbottlegourd"] = new[] { "longbottlegourd", "bottlegourd" },
            ["bittergourd"] = new[] { "bittergourd" },
            ["applegourd"] = new[] { "applegourd", "tinda" },
            ["javaplum"] = new[] { "javaplum", "jamun" },
            ["pineapple"] = new[] { "pineapple" },
            ["melon"] = new[] { "melon" },
            ["papaya"] = new[] { "papaya" },
            ["coconut"] = new[] { "coconut" },
            ["dates"] = new[] { "dates", "date" },
            ["peas"] = new[] { "peas", "pea" },
            ["plum"] = new[] { "plum" },
            ["apricot"] = new[] { "apricot", "appricot" },
            ["sweetpotato"] = new[] { "sweetpotato" },
            ["zucchini"] = new[] { "zucchini", "zuchinni" },
            ["default"] = new[] { "default" },
        };

        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".PNG", ".JPG", ".JPEG", ".WEBP" };

        private static Dictionary<string, string>? _photoIndex;
        private static readonly object _photoIndexLock = new();

        private string? ResolvePhotoPath()
        {
            if (!string.IsNullOrWhiteSpace(ImagePath) && File.Exists(ImagePath))
                return ImagePath;

            var index = GetPhotoIndex();
            if (index.Count == 0) return null;

            // Try aliases for IconKey, then raw item name, then partial contains match
            foreach (var stem in GetLookupStems())
            {
                if (index.TryGetValue(stem, out var path) && File.Exists(path))
                    return path;
            }

            // Fuzzy: any indexed file whose stem contains / is contained by the item name
            var nameKey = NormalizeKey(Name);
            if (nameKey.Length >= 3)
            {
                var fuzzy = index
                    .Where(kv => kv.Key.Contains(nameKey) || nameKey.Contains(kv.Key))
                    .OrderByDescending(kv => kv.Key.Length)
                    .Select(kv => kv.Value)
                    .FirstOrDefault(File.Exists);
                if (fuzzy != null) return fuzzy;
            }

            if (index.TryGetValue("default", out var fallback) && File.Exists(fallback))
                return fallback;

            return null;
        }

        private IEnumerable<string> GetLookupStems()
        {
            var key = IconKey;
            if (PhotoAliases.TryGetValue(key, out var aliases))
            {
                foreach (var a in aliases)
                    yield return NormalizeKey(a);
            }

            yield return NormalizeKey(key);
            yield return NormalizeKey(Name);

            // e.g. "Sweet Potato" → sweetpotato already; also try without spaces raw
            var spaced = (Name ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(spaced))
                yield return NormalizeKey(spaced);
        }

        /// <summary>Letters/digits only lowercase — "Sweet Potato.jpeg" → "sweetpotato".</summary>
        private static string NormalizeKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return new string(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        }

        private static Dictionary<string, string> GetPhotoIndex()
        {
            lock (_photoIndexLock)
            {
                if (_photoIndex != null) return _photoIndex;

                var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var folder in GetProductImageFolders())
                {
                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(folder))
                        {
                            var ext = Path.GetExtension(file);
                            if (!ImageExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)))
                                continue;

                            var stem = NormalizeKey(Path.GetFileNameWithoutExtension(file));
                            if (stem.Length == 0) continue;

                            // Prefer first folder found (output dir first); keep first path per stem
                            if (!index.ContainsKey(stem))
                                index[stem] = file;
                        }
                    }
                    catch
                    {
                        // ignore unreadable folders
                    }
                }

                _photoIndex = index;
                return _photoIndex;
            }
        }

        /// <summary>Output dir first, then walk up for project Assets during local runs.</summary>
        private static IEnumerable<string> GetProductImageFolders()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                var folder = Path.Combine(dir.FullName, "Assets", "Products");
                if (seen.Add(folder) && Directory.Exists(folder))
                    yield return folder;
            }
        }

        /// <summary>Call after adding/replacing product images at runtime.</summary>
        public static void InvalidatePhotoCache()
        {
            lock (_photoIndexLock)
                _photoIndex = null;
            lock (_bitmapCacheLock)
                _bitmapCache.Clear();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

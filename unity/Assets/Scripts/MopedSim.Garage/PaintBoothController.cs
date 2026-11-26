using System;
using System.Collections.Generic;
using MopedSim.Core;
using MopedSim.Data;
using UnityEngine;

namespace MopedSim.Garage
{
    /// <summary>
    /// Curated vintage moped rattle-can paint preset grounded in docs/REFERENCES_VINTAGE_MOPEDS.md.
    /// </summary>
    public readonly struct PaintSwatch
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly string Hex;
        public readonly float DefaultMetallic;
        public readonly float DefaultSmoothness;

        public PaintSwatch(string id, string displayName, string hex, float defaultMetallic, float defaultSmoothness)
        {
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Hex = hex ?? string.Empty;
            DefaultMetallic = Mathf.Clamp01(defaultMetallic);
            DefaultSmoothness = Mathf.Clamp01(defaultSmoothness);
        }
    }

    /// <summary>
    /// Interactive garage rattle-can paint &amp; patina booth (PROJECT_GUIDE.md section 2.5).
    /// Applies live URP color, metallic flake, and weathered patina smoothness across target renderers via a single
    /// cached MaterialPropertyBlock (zero material instantiation, zero per-call GC allocations).
    /// </summary>
    public sealed class PaintBoothController : MonoBehaviour
    {
        public const string DefaultSwatchId = "magnum_red";
        public const string DefaultPaintHex = "#C42828";

        public static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
        public static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
        public static readonly int MetallicPropertyId = Shader.PropertyToID("_Metallic");
        public static readonly int SmoothnessPropertyId = Shader.PropertyToID("_Smoothness");

        // glTFast URP shader graphs (glTF-pbrMetallicRoughness) expose glTF-named properties instead of _BaseColor.
        public static readonly int GltfBaseColorFactorPropertyId = Shader.PropertyToID("baseColorFactor");
        public static readonly int GltfMetallicFactorPropertyId = Shader.PropertyToID("metallicFactor");
        public static readonly int GltfRoughnessFactorPropertyId = Shader.PropertyToID("roughnessFactor");

        private static readonly Color RustOxideTint = new Color(0.45f, 0.26f, 0.16f, 1f);
        private const float MaxPatinaDesaturation = 0.45f;
        private const float MaxPatinaRustBlend = 0.35f;
        private const float MaxPatinaSmoothnessAttenuation = 0.75f;

        private static readonly PaintSwatch[] PresetSwatches = new PaintSwatch[]
        {
            new PaintSwatch("little_yellow", "Little Yellow", "#E8B923", 0.08f, 0.78f),
            new PaintSwatch("magnum_red", "Magnum Red", "#C42828", 0.15f, 0.80f),
            new PaintSwatch("classic_silver", "Classic Silver", "#C0C4C8", 0.65f, 0.76f),
            new PaintSwatch("gloss_black", "Gloss Black", "#1E2024", 0.10f, 0.85f),
            new PaintSwatch("vintage_grey", "Vintage Grey", "#7A8088", 0.12f, 0.58f),
            new PaintSwatch("matte_petg_charcoal", "Matte PETG Charcoal", "#2B2E33", 0.02f, 0.24f),
            new PaintSwatch("bay_teal", "Bay Teal", "#1F7A8C", 0.22f, 0.74f)
        };

        [Header("Scene References")]
        [SerializeField] private Transform _rendererRoot;
        [SerializeField] private BikeVisualAssembler _assembler;

        [Header("Paint Finish")]
        [SerializeField] private string _currentPaintHex = DefaultPaintHex;
        [SerializeField, Range(0f, 1f)] private float _patinaLevel = 0f;
        [SerializeField, Range(0f, 1f)] private float _metallicFlake = 0.15f;
        [SerializeField, Range(0f, 1f)] private float _baseSmoothness = 0.80f;

        private readonly List<Renderer> _rendererScratch = new List<Renderer>(32);
        private readonly HashSet<Transform> _mountedContainersScratch = new HashSet<Transform>();
        private readonly Stack<Transform> _traversalScratch = new Stack<Transform>(32);
        private MaterialPropertyBlock _propertyBlock;
        private Color _currentBaseColor = new Color(0xC4 / 255f, 0x28 / 255f, 0x28 / 255f, 1f);
        private bool _subscribedToAssembler;

        /// <summary>
        /// Raised after paint state changes with (normalizedHex, patinaLevel, metallicFlake).
        /// </summary>
        public event Action<string, float, float> PaintChanged;

        public static IReadOnlyList<PaintSwatch> Swatches
        {
            get { return PresetSwatches; }
        }

        public string CurrentPaintHex
        {
            get { return _currentPaintHex; }
        }

        public float PatinaLevel
        {
            get { return _patinaLevel; }
        }

        public float MetallicFlake
        {
            get { return _metallicFlake; }
        }

        public float BaseSmoothness
        {
            get { return _baseSmoothness; }
        }

        public Color CurrentBaseColor
        {
            get { return _currentBaseColor; }
        }

        public Color EffectiveColor
        {
            get { return ComputeEffectiveColor(_currentBaseColor, _patinaLevel); }
        }

        public float EffectiveSmoothness
        {
            get { return ComputeEffectiveSmoothness(_baseSmoothness, _patinaLevel); }
        }

        public Transform RendererRoot
        {
            get { return ResolveTargetRoot(); }
        }

        public BikeVisualAssembler Assembler
        {
            get { return _assembler; }
        }

        /// <summary>
        /// Returns true only for paintable painted-body slots (Frame, FuelTank, FrontFender).
        /// Mechanical, rubber, chrome, and control parts (wheels, tires, exhaust, cylinder head, carb, bars, etc.)
        /// are never tinted by the paint booth.
        /// </summary>
        public static bool IsPaintableBodySlot(string slotId)
        {
            if (string.IsNullOrEmpty(slotId))
            {
                return false;
            }

            return string.Equals(slotId, SlotIds.Frame, StringComparison.Ordinal)
                || string.Equals(slotId, SlotIds.FuelTank, StringComparison.Ordinal)
                || string.Equals(slotId, SlotIds.FrontFender, StringComparison.Ordinal);
        }

        /// <summary>
        /// Looks up a curated rattle-can swatch by ID (case-insensitive).
        /// </summary>
        public static bool TryGetSwatch(string swatchId, out PaintSwatch swatch)
        {
            if (!string.IsNullOrEmpty(swatchId))
            {
                for (int i = 0; i < PresetSwatches.Length; i++)
                {
                    if (string.Equals(PresetSwatches[i].Id, swatchId, StringComparison.OrdinalIgnoreCase))
                    {
                        swatch = PresetSwatches[i];
                        return true;
                    }
                }
            }

            swatch = default(PaintSwatch);
            return false;
        }

        /// <summary>
        /// Parses and normalizes a 6-digit RGB hex color in either "#RRGGBB" or "RRGGBB" form.
        /// Rejects empty, malformed, 3-digit, or alpha-channel strings cleanly without allocations on failure.
        /// </summary>
        public static bool TryParsePaintHex(string input, out string normalizedHex, out Color color)
        {
            normalizedHex = string.Empty;
            color = Color.white;

            if (string.IsNullOrEmpty(input))
            {
                return false;
            }

            int start = 0;
            int end = input.Length;
            while (start < end && char.IsWhiteSpace(input[start]))
            {
                start++;
            }

            while (end > start && char.IsWhiteSpace(input[end - 1]))
            {
                end--;
            }

            if (start < end && input[start] == '#')
            {
                start++;
            }

            if (end - start != 6)
            {
                return false;
            }

            int rHi = HexDigitToInt(input[start]);
            int rLo = HexDigitToInt(input[start + 1]);
            int gHi = HexDigitToInt(input[start + 2]);
            int gLo = HexDigitToInt(input[start + 3]);
            int bHi = HexDigitToInt(input[start + 4]);
            int bLo = HexDigitToInt(input[start + 5]);

            if (rHi < 0 || rLo < 0 || gHi < 0 || gLo < 0 || bHi < 0 || bLo < 0)
            {
                return false;
            }

            int r = (rHi << 4) | rLo;
            int g = (gHi << 4) | gLo;
            int b = (bHi << 4) | bLo;

            char[] chars = new char[7];
            chars[0] = '#';
            chars[1] = IntToUpperHex(rHi);
            chars[2] = IntToUpperHex(rLo);
            chars[3] = IntToUpperHex(gHi);
            chars[4] = IntToUpperHex(gLo);
            chars[5] = IntToUpperHex(bHi);
            chars[6] = IntToUpperHex(bLo);

            normalizedHex = new string(chars);
            color = new Color(r / 255f, g / 255f, b / 255f, 1f);
            return true;
        }

        /// <summary>
        /// Computes weathered base color by desaturating slightly and blending toward warm rust-oxide with patina.
        /// </summary>
        public static Color ComputeEffectiveColor(Color baseColor, float patinaLevel)
        {
            float p = ClampUnit(patinaLevel);
            if (p <= 0f)
            {
                return new Color(baseColor.r, baseColor.g, baseColor.b, 1f);
            }

            float luma = 0.2126f * baseColor.r + 0.7152f * baseColor.g + 0.0722f * baseColor.b;
            Color grayscale = new Color(luma, luma, luma, 1f);
            Color desaturated = Color.Lerp(baseColor, grayscale, p * MaxPatinaDesaturation);
            Color weathered = Color.Lerp(desaturated, RustOxideTint, p * MaxPatinaRustBlend);
            weathered.a = 1f;
            return weathered;
        }

        /// <summary>
        /// Attenuates surface smoothness as weathered patina increases.
        /// </summary>
        public static float ComputeEffectiveSmoothness(float baseSmoothness, float patinaLevel)
        {
            float s = ClampUnit(baseSmoothness);
            float p = ClampUnit(patinaLevel);
            return Mathf.Clamp01(s * (1f - p * MaxPatinaSmoothnessAttenuation));
        }

        private void Awake()
        {
            string normalized;
            Color parsed;
            if (TryParsePaintHex(_currentPaintHex, out normalized, out parsed))
            {
                _currentPaintHex = normalized;
                _currentBaseColor = parsed;
            }
            else
            {
                TryParsePaintHex(DefaultPaintHex, out _currentPaintHex, out _currentBaseColor);
            }

            _patinaLevel = ClampUnit(_patinaLevel);
            _metallicFlake = ClampUnit(_metallicFlake);
            _baseSmoothness = ClampUnit(_baseSmoothness);
        }

        private void OnEnable()
        {
            SubscribeAssembler();
            ApplyCurrentPaint();
        }

        private void OnDisable()
        {
            UnsubscribeAssembler();
        }

        private void OnDestroy()
        {
            UnsubscribeAssembler();
        }

        /// <summary>
        /// Sets the transform hierarchy whose Renderers receive paint updates and applies the active finish immediately.
        /// </summary>
        public void SetRendererRoot(Transform root)
        {
            _rendererRoot = root;
            ApplyCurrentPaint();
        }

        /// <summary>
        /// Binds to a BikeVisualAssembler so mounted and newly added bike parts receive live paint updates.
        /// </summary>
        public void Bind(BikeVisualAssembler assembler)
        {
            if (_assembler == assembler)
            {
                if (_assembler != null)
                {
                    SubscribeAssembler();
                    _rendererRoot = _assembler.AssemblyRoot;
                    ApplyCurrentPaint();
                }

                return;
            }

            UnsubscribeAssembler();
            _assembler = assembler;
            _rendererRoot = _assembler != null ? _assembler.AssemblyRoot : null;
            SubscribeAssembler();
            ApplyCurrentPaint();
        }

        /// <summary>
        /// Applies a curated rattle-can swatch by ID, adopting its hex, default metallic flake, and base smoothness
        /// while preserving the current patina level.
        /// </summary>
        public bool ApplySwatch(string swatchId)
        {
            PaintSwatch swatch;
            if (!TryGetSwatch(swatchId, out swatch))
            {
                return false;
            }

            _baseSmoothness = ClampUnit(swatch.DefaultSmoothness);
            return SetPaint(swatch.Hex, _patinaLevel, swatch.DefaultMetallic);
        }

        /// <summary>
        /// Validates and applies a custom hex paint, patina level (0..1), and metallic flake (0..1).
        /// Returns false without modifying state or raising PaintChanged when hex is invalid.
        /// </summary>
        public bool SetPaint(string hex, float patinaLevel, float metallicFlake)
        {
            string normalized;
            Color parsed;
            if (!TryParsePaintHex(hex, out normalized, out parsed))
            {
                return false;
            }

            _currentPaintHex = normalized;
            _currentBaseColor = parsed;
            _patinaLevel = ClampUnit(patinaLevel);
            _metallicFlake = ClampUnit(metallicFlake);

            ApplyCurrentPaint();
            RaisePaintChanged();
            return true;
        }

        /// <summary>
        /// Writes the active paint finish into a persistent BikeSaveState record.
        /// </summary>
        public void ApplyToSave(BikeSaveState bikeSave)
        {
            if (bikeSave == null)
            {
                throw new ArgumentNullException(nameof(bikeSave));
            }

            bikeSave.PaintHex = _currentPaintHex;
            bikeSave.PatinaLevel = _patinaLevel;
        }

        /// <summary>
        /// Pushes the active paint finish into the bound assembler's paintable body slots (Frame, FuelTank, FrontFender),
        /// or into all Renderers under the explicit renderer root when no assembler is bound.
        /// </summary>
        public int ApplyCurrentPaint()
        {
            if (_assembler != null)
            {
                return ApplyToAssembler();
            }

            if (_rendererRoot != null)
            {
                return ApplyToHierarchy(_rendererRoot);
            }

            return 0;
        }

        /// <summary>
        /// Applies the active paint finish only to mounted paintable body slots on the bound assembler,
        /// without tinting child mechanical slots attached to their SOCKET_* transforms.
        /// </summary>
        public int ApplyToAssembler()
        {
            if (_assembler == null)
            {
                return 0;
            }

            PopulateMountedContainersScratch();
            IReadOnlyList<string> mountedSlotIds = _assembler.MountedSlotIds;
            int updated = 0;
            for (int i = 0; i < mountedSlotIds.Count; i++)
            {
                string slotId = mountedSlotIds[i];
                if (!IsPaintableBodySlot(slotId))
                {
                    continue;
                }

                GameObject mounted;
                if (_assembler.TryGetMountedPart(slotId, out mounted) && mounted != null)
                {
                    updated += ApplyPaintToSlotOwnRenderers(mounted.transform);
                }
            }

            _mountedContainersScratch.Clear();
            return updated;
        }

        /// <summary>
        /// Pushes the active paint finish into all Renderers under <paramref name="root"/> via one cached MaterialPropertyBlock.
        /// </summary>
        public int ApplyToHierarchy(Transform root)
        {
            return ApplyPaintToHierarchy(root);
        }

        private int ApplyPaintToHierarchy(Transform hierarchyRoot)
        {
            if (hierarchyRoot == null)
            {
                return 0;
            }

            EnsurePropertyBlock();
            Color effectiveColor = ComputeEffectiveColor(_currentBaseColor, _patinaLevel);
            float effectiveSmoothness = ComputeEffectiveSmoothness(_baseSmoothness, _patinaLevel);

            _rendererScratch.Clear();
            hierarchyRoot.GetComponentsInChildren<Renderer>(true, _rendererScratch);

            int updated = 0;
            for (int i = 0; i < _rendererScratch.Count; i++)
            {
                Renderer rend = _rendererScratch[i];
                if (rend == null)
                {
                    continue;
                }

                ApplyPropertyBlockToRenderer(rend, effectiveColor, effectiveSmoothness);
                updated++;
            }

            _rendererScratch.Clear();
            return updated;
        }

        private int ApplyPaintToSlotOwnRenderers(Transform slotRoot)
        {
            if (slotRoot == null)
            {
                return 0;
            }

            EnsurePropertyBlock();
            Color effectiveColor = ComputeEffectiveColor(_currentBaseColor, _patinaLevel);
            float effectiveSmoothness = ComputeEffectiveSmoothness(_baseSmoothness, _patinaLevel);

            int updated = 0;
            _traversalScratch.Clear();
            _traversalScratch.Push(slotRoot);

            while (_traversalScratch.Count > 0)
            {
                Transform current = _traversalScratch.Pop();
                if (current == null)
                {
                    continue;
                }

                Renderer rend = current.GetComponent<Renderer>();
                if (rend != null)
                {
                    ApplyPropertyBlockToRenderer(rend, effectiveColor, effectiveSmoothness);
                    updated++;
                }

                int childCount = current.childCount;
                for (int i = 0; i < childCount; i++)
                {
                    Transform child = current.GetChild(i);
                    if (child == null)
                    {
                        continue;
                    }

                    // Do not descend into child mounted slots attached to this part's SOCKET_* transforms.
                    if (child != slotRoot && _mountedContainersScratch.Contains(child))
                    {
                        continue;
                    }

                    _traversalScratch.Push(child);
                }
            }

            _traversalScratch.Clear();
            return updated;
        }

        private void PopulateMountedContainersScratch()
        {
            _mountedContainersScratch.Clear();
            if (_assembler == null)
            {
                return;
            }

            IReadOnlyList<string> mountedSlotIds = _assembler.MountedSlotIds;
            for (int i = 0; i < mountedSlotIds.Count; i++)
            {
                GameObject mounted;
                if (_assembler.TryGetMountedPart(mountedSlotIds[i], out mounted) && mounted != null)
                {
                    _mountedContainersScratch.Add(mounted.transform);
                }
            }
        }

        private void EnsurePropertyBlock()
        {
            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }
        }

        private void ApplyPropertyBlockToRenderer(Renderer rend, Color effectiveColor, float effectiveSmoothness)
        {
            rend.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorPropertyId, effectiveColor);
            _propertyBlock.SetColor(ColorPropertyId, effectiveColor);
            _propertyBlock.SetFloat(MetallicPropertyId, _metallicFlake);
            _propertyBlock.SetFloat(SmoothnessPropertyId, effectiveSmoothness);
            _propertyBlock.SetColor(GltfBaseColorFactorPropertyId, effectiveColor);
            _propertyBlock.SetFloat(GltfMetallicFactorPropertyId, _metallicFlake);
            _propertyBlock.SetFloat(GltfRoughnessFactorPropertyId, 1f - effectiveSmoothness);
            rend.SetPropertyBlock(_propertyBlock);
        }

        private Transform ResolveTargetRoot()
        {
            if (_rendererRoot != null)
            {
                return _rendererRoot;
            }

            return _assembler != null ? _assembler.AssemblyRoot : null;
        }

        private void SubscribeAssembler()
        {
            if (_assembler != null && !_subscribedToAssembler)
            {
                _assembler.PartMounted += OnAssemblerPartMounted;
                _subscribedToAssembler = true;
            }
        }

        private void UnsubscribeAssembler()
        {
            if (_assembler != null && _subscribedToAssembler)
            {
                _assembler.PartMounted -= OnAssemblerPartMounted;
            }

            _subscribedToAssembler = false;
        }

        private void OnAssemblerPartMounted(string slotId)
        {
            if (_assembler == null || !IsPaintableBodySlot(slotId))
            {
                return;
            }

            GameObject mounted;
            if (_assembler.TryGetMountedPart(slotId, out mounted) && mounted != null)
            {
                PopulateMountedContainersScratch();
                ApplyPaintToSlotOwnRenderers(mounted.transform);
                _mountedContainersScratch.Clear();
            }
        }

        private void RaisePaintChanged()
        {
            Action<string, float, float> handler = PaintChanged;
            if (handler != null)
            {
                handler.Invoke(_currentPaintHex, _patinaLevel, _metallicFlake);
            }
        }

        private static float ClampUnit(float value)
        {
            if (float.IsNaN(value) || float.IsNegativeInfinity(value))
            {
                return 0f;
            }

            if (float.IsPositiveInfinity(value))
            {
                return 1f;
            }

            return Mathf.Clamp01(value);
        }

        private static int HexDigitToInt(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return c - '0';
            }

            if (c >= 'a' && c <= 'f')
            {
                return 10 + (c - 'a');
            }

            if (c >= 'A' && c <= 'F')
            {
                return 10 + (c - 'A');
            }

            return -1;
        }

        private static char IntToUpperHex(int value)
        {
            return value < 10 ? (char)('0' + value) : (char)('A' + (value - 10));
        }
    }
}

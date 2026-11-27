using UnityEngine;

namespace MopedSim.World
{
    /// <summary>
    /// World-space guidance toward the active gig's next checkpoint: a tall emissive light pillar on the target plus a small 3D
    /// arrow hovering ahead of the rider pointing at it, with the distance in metres on a billboarded TextMesh. Hidden when no
    /// gig is active. Presentation only: reads <see cref="CourierGigController"/> state, never changes it. Self-installs at
    /// runtime (no scene wiring) and is allocation-free per frame once the distance strings are cached.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ObjectiveGuidance : MonoBehaviour
    {
        private const float PillarHeight = 90f;
        private const float PillarWidth = 1.2f;
        private const float ArrowLead = 3.5f;
        private const float ArrowHeight = 2.4f;
        private const int MaxDistanceMeters = 20000;
        private const float FindIntervalSeconds = 1f;

        private static readonly Color GuideColor = new Color(1f, 0.82f, 0.15f, 1f);

        private static ObjectiveGuidance _instance;
        private static readonly string[] DistanceStrings = new string[MaxDistanceMeters + 1];

        private CourierGigController _courier;
        private float _nextFindTime;
        private Transform _pillar;
        private Transform _arrowRoot;
        private TextMesh _label;
        private Transform _labelTransform;
        private Material _material;
        private int _lastMeters = -1;
        private bool _visible;
        private bool _beaconMaterialApplied;
        private Camera _camera;

        /// <summary>True while the pillar and arrow are shown (an active gig has a next checkpoint).</summary>
        public bool IsVisible
        {
            get { return _visible; }
        }

        /// <summary>The marker pillar transform (null until first built).</summary>
        public Transform Pillar
        {
            get { return _pillar; }
        }

        public static ObjectiveGuidance Instance
        {
            get { return _instance; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null)
            {
                return;
            }

            GameObject go = new GameObject("ObjectiveGuidance");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ObjectiveGuidance>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            Build();
            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }

            if (_material != null)
            {
                Destroy(_material);
            }
        }

        /// <summary>Binds an explicit courier (otherwise the first one in the loaded scenes is found lazily).</summary>
        public void Bind(CourierGigController courier)
        {
            _courier = courier;
        }

        private void LateUpdate()
        {
            if (_courier == null)
            {
                if (Time.unscaledTime >= _nextFindTime)
                {
                    _nextFindTime = Time.unscaledTime + FindIntervalSeconds;
                    _courier = FindAnyObjectByType<CourierGigController>();
                }

                SetVisible(false);
                return;
            }

            Vector3 target;
            Transform rider = _courier.Rider;
            if (rider == null || !_courier.HasActiveJob || !_courier.TryGetNextCheckpointPosition(out target))
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);
            ResolveBeaconMaterial();
            Vector3 riderPos = rider.position;
            _pillar.position = new Vector3(target.x, target.y + PillarHeight * 0.5f, target.z);

            Vector3 toTarget = target - riderPos;
            float flat = Mathf.Sqrt(toTarget.x * toTarget.x + toTarget.z * toTarget.z);
            Vector3 dir = flat > 0.01f ? new Vector3(toTarget.x / flat, 0f, toTarget.z / flat) : Vector3.forward;
            float lead = Mathf.Min(ArrowLead, flat);
            Vector3 arrowPos = new Vector3(riderPos.x + dir.x * lead, riderPos.y + ArrowHeight, riderPos.z + dir.z * lead);
            _arrowRoot.position = arrowPos;
            _arrowRoot.rotation = Quaternion.LookRotation(dir, Vector3.up);

            int meters = Mathf.Min(MaxDistanceMeters, Mathf.RoundToInt(toTarget.magnitude));
            if (meters != _lastMeters)
            {
                _lastMeters = meters;
                string text = DistanceStrings[meters];
                if (text == null)
                {
                    text = meters.ToString() + " m";
                    DistanceStrings[meters] = text;
                }

                _label.text = text;
            }

            if (_camera == null || !_camera.isActiveAndEnabled)
            {
                _camera = Camera.main;
            }

            if (_camera != null)
            {
                Vector3 away = _labelTransform.position - _camera.transform.position;
                if (away.sqrMagnitude > 0.0001f)
                {
                    _labelTransform.rotation = Quaternion.LookRotation(away, Vector3.up);
                }
            }
        }

        /// <summary>Reuses the delivery beacon's imported emissive material (known to render in player builds) once available.</summary>
        private void ResolveBeaconMaterial()
        {
            if (_beaconMaterialApplied)
            {
                return;
            }

            _beaconMaterialApplied = true;
            Transform beacon = _courier.NextCheckpointBeacon;
            if (beacon == null)
            {
                return;
            }

            Material glow = null;
            MeshRenderer[] beaconRenderers = beacon.GetComponentsInChildren<MeshRenderer>(true);
            for (int b = 0; b < beaconRenderers.Length && glow == null; b++)
            {
                Material[] mats = beaconRenderers[b].sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] != null && mats[m].HasProperty("_EmissionColor") && mats[m].GetColor("_EmissionColor").maxColorComponent > 0.1f)
                    {
                        glow = mats[m];
                        break;
                    }
                }
            }

            if (glow == null)
            {
                return;
            }

            MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].gameObject.name != "DistanceLabel")
                {
                    renderers[i].sharedMaterial = glow;
                }
            }
        }

        private void SetVisible(bool visible)
        {
            if (_visible == visible && _pillar != null && _pillar.gameObject.activeSelf == visible)
            {
                return;
            }

            _visible = visible;
            if (_pillar != null)
            {
                _pillar.gameObject.SetActive(visible);
            }

            if (_arrowRoot != null)
            {
                _arrowRoot.gameObject.SetActive(visible);
            }

            if (!visible)
            {
                _lastMeters = -1;
            }
        }

        private void Build()
        {
            _material = CreateGlowMaterial();

            _pillar = CreateBlock("GuidancePillar", null, new Vector3(PillarWidth, PillarHeight, PillarWidth)).transform;
            _pillar.SetParent(transform, false);

            _arrowRoot = new GameObject("GuidanceArrow").transform;
            _arrowRoot.SetParent(transform, false);
            CreateBlock("Shaft", _arrowRoot, new Vector3(0.14f, 0.14f, 0.9f)).transform.localPosition = new Vector3(0f, 0f, -0.2f);
            Transform head = CreateBlock("Head", _arrowRoot, new Vector3(0.5f, 0.14f, 0.5f)).transform;
            head.localPosition = new Vector3(0f, 0f, 0.45f);
            head.localRotation = Quaternion.Euler(0f, 45f, 0f);

            GameObject labelGo = new GameObject("DistanceLabel");
            _labelTransform = labelGo.transform;
            _labelTransform.SetParent(_arrowRoot, false);
            _labelTransform.localPosition = new Vector3(0f, 0.6f, 0f);
            _label = labelGo.AddComponent<TextMesh>();
            _label.anchor = TextAnchor.MiddleCenter;
            _label.alignment = TextAlignment.Center;
            _label.characterSize = 0.04f;
            _label.fontSize = 64;
            _label.color = Color.white;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                _label.font = font;
                labelGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
        }

        private GameObject CreateBlock(string name, Transform parent, Vector3 scale)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Collider col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            go.transform.localScale = scale;
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        private static Material CreateGlowMaterial()
        {
            // Pipeline default material (URP Lit): Shader.Find can return null (stripped) in player builds and the primitive's
            // built-in default material renders magenta under URP.
            Material source = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.defaultMaterial
                : null;
            if (source == null)
            {
                GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                source = probe.GetComponent<MeshRenderer>().sharedMaterial;
                Destroy(probe);
            }

            Material m = new Material(source);
            // No emission keyword: the _EMISSION variant is stripped from player builds that never use it, rendering magenta.
            m.name = "ObjectiveGuidanceGlow";
            m.color = GuideColor;
            if (m.HasProperty("_BaseColor"))
            {
                m.SetColor("_BaseColor", GuideColor);
            }

            return m;
        }
    }
}

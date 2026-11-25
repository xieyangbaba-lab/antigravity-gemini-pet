using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace AntigravityGeminiPet
{
    public class Spring1D
    {
        public double Value;
        public double Vel;
        public double Stiffness;
        public double Damping;
        public double MaxValue;
        public double MaxVel;

        public Spring1D(double stiffness, double damping, double maxValue, double maxVel)
        {
            Stiffness = stiffness;
            Damping = damping;
            MaxValue = maxValue;
            MaxVel = maxVel;
        }

        public void Step(double target, double impulse, double dt)
        {
            // Frame-rate independent impulse + hard velocity & amplitude thresholds so fast dragging NEVER explodes hair/skirt!
            double clampedTarget = Math.Max(-MaxValue, Math.Min(MaxValue, target));
            double impulseScale = Math.Min(1.5, dt * 60.0);
            Vel += impulse * impulseScale;
            if (Vel > MaxVel) Vel = MaxVel;
            else if (Vel < -MaxVel) Vel = -MaxVel;

            // Sub-step semi-implicit Euler to guarantee unconditional stability even on lag spikes!
            int subSteps = (dt > 0.018) ? 3 : 2;
            double h = dt / subSteps;
            for (int i = 0; i < subSteps; i++)
            {
                double acc = (clampedTarget - Value) * Stiffness - Vel * Damping;
                Vel += acc * h;
                if (Vel > MaxVel) Vel = MaxVel;
                else if (Vel < -MaxVel) Vel = -MaxVel;
                Value += Vel * h;
                if (Value > MaxValue)
                {
                    Value = MaxValue;
                    if (Vel > 0.0) Vel *= 0.25;
                }
                else if (Value < -MaxValue)
                {
                    Value = -MaxValue;
                    if (Vel < 0.0) Vel *= 0.25;
                }
            }
        }
    }

    public class ArtMeshLayer
    {
        public string Name;
        public int GridX;
        public int GridY;
        public double X0, Y0, W, H;
        public double ZDepth;
        public double[] RestX;
        public double[] RestY;
        public MeshGeometry3D Mesh;
        public GeometryModel3D Model;

        public ArtMeshLayer(string name, BitmapSource fullBmp, int gridX, int gridY, double zDepth)
        {
            Name = name;
            GridX = gridX;
            GridY = gridY;
            ZDepth = zDepth;

            Int32Rect bbox = FindTightBounds(fullBmp, 4);
            X0 = bbox.X;
            Y0 = bbox.Y;
            W = bbox.Width;
            H = bbox.Height;

            CroppedBitmap cropped = new CroppedBitmap(fullBmp, bbox);
            cropped.Freeze();

            Mesh = new MeshGeometry3D();
            int count = (gridX + 1) * (gridY + 1);
            RestX = new double[count];
            RestY = new double[count];

            Point3DCollection positions = new Point3DCollection(count);
            PointCollection texCoords = new PointCollection(count);
            Vector3DCollection normals = new Vector3DCollection(count);

            for (int iy = 0; iy <= gridY; iy++)
            {
                double v = (double)iy / gridY;
                double py = Y0 + v * H;
                for (int ix = 0; ix <= gridX; ix++)
                {
                    double u = (double)ix / gridX;
                    double px = X0 + u * W;
                    int idx = iy * (gridX + 1) + ix;
                    RestX[idx] = px;
                    RestY[idx] = py;
                    positions.Add(new Point3D(px, -py, zDepth));
                    texCoords.Add(new Point(u, v));
                    normals.Add(new Vector3D(0, 0, 1));
                }
            }

            Int32Collection indices = new Int32Collection(gridX * gridY * 6);
            for (int iy = 0; iy < gridY; iy++)
            {
                for (int ix = 0; ix < gridX; ix++)
                {
                    int a = iy * (gridX + 1) + ix;
                    int b = a + 1;
                    int c = (iy + 1) * (gridX + 1) + ix;
                    int d = c + 1;
                    indices.Add(a); indices.Add(c); indices.Add(b);
                    indices.Add(b); indices.Add(c); indices.Add(d);
                }
            }

            Mesh.Positions = positions;
            Mesh.TextureCoordinates = texCoords;
            Mesh.Normals = normals;
            Mesh.TriangleIndices = indices;

            ImageBrush brush = new ImageBrush(cropped);
            brush.Stretch = Stretch.Fill;
            brush.ViewportUnits = BrushMappingMode.RelativeToBoundingBox;
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
            brush.Freeze();

            DiffuseMaterial mat = new DiffuseMaterial(brush);
            mat.AmbientColor = Colors.White;
            mat.Color = Colors.White;
            mat.Freeze();

            Model = new GeometryModel3D(Mesh, mat);
        }

        public static Int32Rect FindTightBounds(BitmapSource src, int pad)
        {
            int w = src.PixelWidth;
            int h = src.PixelHeight;
            int stride = w * 4;
            byte[] pixels = new byte[stride * h];
            src.CopyPixels(pixels, stride, 0);

            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                for (int x = 0; x < w; x++)
                {
                    if (pixels[row + x * 4 + 3] > 6)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            if (maxX < minX) return new Int32Rect(0, 0, Math.Min(16, w), Math.Min(16, h));
            int rx0 = Math.Max(0, minX - pad);
            int ry0 = Math.Max(0, minY - pad);
            int rx1 = Math.Min(w - 1, maxX + pad);
            int ry1 = Math.Min(h - 1, maxY + pad);
            return new Int32Rect(rx0, ry0, rx1 - rx0 + 1, ry1 - ry0 + 1);
        }

        public void Deform(Func<double, double, Point> warpFunc)
        {
            Point3DCollection pts = Mesh.Positions;
            int n = RestX.Length;
            for (int i = 0; i < n; i++)
            {
                Point p = warpFunc(RestX[i], RestY[i]);
                pts[i] = new Point3D(p.X, -p.Y, ZDepth);
            }
        }
    }

    public class Particle
    {
        public TextBlock Visual;
        public double X, Y, Vx, Vy, Life, MaxLife;
    }

    public class Live2DPetWindow : Window
    {
        private const int UdpPort = 47291;
        private const double CanvasSize = 768.0;
        // Generous width & top jump headroom so 4-limb Happy Star-Jump never clips!
        private const double BaseWinW = 396.0;
        private const double BaseWinH = 556.0;

        // Exact anatomical anchors in 768x768 PSD space
        private const double PivotFeetX = 384.0;
        private const double PivotFeetY = 755.0;
        private const double PivotWaistX = 384.0;
        private const double PivotWaistY = 345.0;
        private const double PivotHeadX = 383.0;
        private const double PivotHeadY = 166.0;
        private const double FaceCenterX = 383.0;
        private const double FaceCenterY = 138.0;

        private const double EyeRCX = 359.5;
        private const double EyeRCY = 139.0;
        private const double EyeLCX = 407.0;
        private const double EyeLCY = 139.0;

        // 3-Bone Arm Joints (Shoulder -> Elbow -> Wrist Cuff Center!)
        private const double ShoulderRX = 332.0;
        private const double ShoulderRY = 224.0;
        private const double ElbowRX = 302.0;
        private const double ElbowRY = 314.0;
        private const double WristRX = 264.0;
        private const double WristRY = 394.0;

        private const double ShoulderLX = 434.0;
        private const double ShoulderLY = 224.0;
        private const double ElbowLX = 464.0;
        private const double ElbowLY = 314.0;
        private const double WristLX = 502.0;
        private const double WristLY = 394.0;

        // Rest forearm angle from straight down (+Y): atan2(36, 78) = 0.432 rad (24.78 deg)
        private const double RestForearmRad = 0.432;

        // Independent Left & Right Leg Joints (Hip -> Knee)
        private const double HipRX = 346.0;
        private const double HipRY = 408.0;
        private const double KneeRX = 346.0;
        private const double KneeRY = 558.0;

        private const double HipLX = 418.0;
        private const double HipLY = 408.0;
        private const double KneeLX = 418.0;
        private const double KneeLY = 558.0;

        private const double TailRootX = 406.0;
        private const double TailRootY = 422.0;
        private const double EarRPivotX = 349.0;
        private const double EarRPivotY = 86.0;
        private const double EarLPivotX = 417.0;
        private const double EarLPivotY = 86.0;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private readonly string _baseDir;
        private readonly string _layersDir;
        private readonly string _settingsPath;

        private readonly Dictionary<string, BitmapSource> _layerBitmaps = new Dictionary<string, BitmapSource>();
        private readonly Dictionary<string, BitmapSource> _handBitmaps = new Dictionary<string, BitmapSource>();
        private readonly Dictionary<string, ArtMeshLayer> _meshes = new Dictionary<string, ArtMeshLayer>();
        private readonly Random _rng = new Random();

        // Visual tree
        private Canvas _outerCanvas;
        private Grid _rigStage;
        private TranslateTransform _stageBounceShift;
        private ScaleTransform _stageSquashScale;

        // Dynamic Back vs Front Arm Z-ordering groups!
        private Model3DGroup _backBodyGroup;   // Back hair, tail, legs, behind-back arms!
        private Model3DGroup _frontArmGroup;   // Front arms (in front of torso & skirt!)
        private bool _armsBehindBack = false;

        // Separate Swappable Articulated Hand Models + Reusable Transforms (zero per-frame allocations!)
        private Canvas _handCanvas;
        private Image _handRImg;
        private Image _handLImg;
        private RotateTransform _handRRot;
        private RotateTransform _handLRot;
        private string _curHandR = "relaxed";
        private string _curHandL = "relaxed";

        // Reusable Facial Rig Transforms (eliminates ~600 WPF object allocations/sec!)
        private readonly Dictionary<UIElement, RotateTransform> _faceRotCache = new Dictionary<UIElement, RotateTransform>();
        private readonly Dictionary<UIElement, TranslateTransform> _faceShiftCache = new Dictionary<UIElement, TranslateTransform>();
        private TransformGroup _mouthOverlayTg;
        private RotateTransform _mouthOverlayRot;
        private TranslateTransform _mouthOverlayShift;
        private TranslateTransform _thinkMouthShift;
        private TranslateTransform _blushShift;
        private double _cachedBubbleHeight = 68.0;

        // Facial rig UIElements
        private Canvas _faceCanvas;
        private Image _noseImg;
        private Image _mouthImg;
        private Canvas _happyMouthOverlay;
        private Canvas _surprisedMouthOverlay;
        private Canvas _poutMouthOverlay;
        private Ellipse _thinkMouthOverlay;
        private Ellipse _blushL;
        private Ellipse _blushR;

        // Triple-locked GPU-clipped Eyes
        private Grid _eyeRRoot;
        private Grid _eyeRMaskGroup;
        private ScaleTransform _eyeROpenScale;
        private TranslateTransform _eyeRIrisShift;
        private ScaleTransform _eyeRIrisScale;
        private Image _eyelashRImg;
        private TranslateTransform _eyelashRShift;
        private ScaleTransform _eyelashRScale;
        private System.Windows.Shapes.Path _smileLashR;
        private System.Windows.Shapes.Path _sleepLashR;

        private Grid _eyeLRoot;
        private Grid _eyeLMaskGroup;
        private ScaleTransform _eyeLOpenScale;
        private TranslateTransform _eyeLIrisShift;
        private ScaleTransform _eyeLIrisScale;
        private Image _eyelashLImg;
        private TranslateTransform _eyelashLShift;
        private ScaleTransform _eyelashLScale;
        private System.Windows.Shapes.Path _smileLashL;
        private System.Windows.Shapes.Path _sleepLashL;

        private Image _browRImg;
        private Image _browLImg;

        // Custom Conjured Gemini Magic Wand + Wand-Tip Magic Halo & Summoning Flash!
        private BitmapSource _wandBitmap;
        private Image _wandImg;
        private ScaleTransform _wandScale;
        private RotateTransform _wandRot;
        private TranslateTransform _wandShift;

        private Canvas _magicOrbGroup;
        private Canvas _wandTipNode;
        private RotateTransform _magicOrbRot;
        private ScaleTransform _magicOrbScale;
        private Canvas _summonFlashNode;
        private RotateTransform _summonFlashRot;
        private ScaleTransform _summonFlashScale;
        private double _lastWandTipX = 160.0;
        private double _lastWandTipY = 120.0;

        private Canvas _particleCanvas;
        private Grid _bubbleHost;
        private StackPanel _bubbleContainer;
        private Border _bubbleBorder;
        private TextBlock _bubbleText;
        private ResourceDictionary _animeMenuRes;
        private readonly List<Particle> _particles = new List<Particle>();

        // Full 4-Limb + Hand + Face Live2D Parameters
        public double ParamAngleX = 0.0;
        public double ParamAngleY = 0.0;
        public double ParamAngleZ = 0.0;
        public double ParamBodyAngleX = 0.0;
        public double ParamBodyAngleZ = 0.0;
        public double ParamBodyShiftX = 0.0;
        public double ParamJumpY = 0.0;       // Airborne leap height (pixels upward in 768 space!)

        public double ParamEyeBallX = 0.0;
        public double ParamEyeBallY = 0.0;
        public double ParamEyeROpen = 1.0;
        public double ParamEyeLOpen = 1.0;
        public double ParamEyeRSmile = 0.0;
        public double ParamEyeLSmile = 0.0;
        public double ParamEyeSleep = 0.0;    // Peaceful closed u u sleeping lashes!
        public double ParamIrisScale = 1.0;
        public double ParamMouthOpen = 0.0;
        public double ParamMouthPout = 0.0;
        public double ParamMouthSurprised = 0.0;
        public double ParamMouthGrumpy = 0.0;
        public double ParamBrowY = 0.0;
        public double ParamBrowRAngle = 0.0;
        public double ParamBrowLAngle = 0.0;

        // 3-Bone Articulated Arms + Swappable Hand Shapes
        public double ParamUpperArmR = 0.0;
        public double ParamElbowR = 0.0;
        public double ParamWristR = 0.0;
        public double ParamUpperArmL = 0.0;
        public double ParamElbowL = 0.0;
        public double ParamWristL = 0.0;
        public bool ParamArmsBehind = false;
        public bool ParamHipLockR = false;    // True -> Right hand locked rock-steady gripping waist (zero wobble!)
        public bool ParamHipLockL = false;    // True -> Left hand locked rock-steady gripping waist (zero wobble!)
        public string ParamHandR = "relaxed";
        public string ParamHandL = "relaxed";

        // 2-Bone Independent Anatomical Legs (Hip spread + Knee fold + 3D foreshortening + Squat cushion!)
        public double ParamLegSpreadR = 0.0;  // + outward hip angle, - shy inward
        public double ParamLegKneeR = 0.0;    // + outward-upward knee fold rad
        public double ParamLegBendR = 0.0;    // 0..1 airborne backward knee flexion (foreshortening + lift)
        public double ParamLegSpreadL = 0.0;
        public double ParamLegKneeL = 0.0;
        public double ParamLegBendL = 0.0;
        public double ParamLegSquat = 0.0;    // 0..1 grounded squat anticipation / landing cushion!

        public double ParamEarPerk = 0.0;
        public double ParamStretch = 0.0;
        public double ParamBreath = 0.0;
        public double ParamMagicGlow = 0.0;
        public double ParamWandShow = 0.0;       // 0..1 conjured magic wand opacity
        public double ParamWandScale = 0.0;      // 0..1.15 elastic conjure scale-in
        public double ParamWandFlash = 0.0;      // 0..1 summoning circle flash burst at hand
        public double ParamWandAngle = -18.0;    // wand swing angle in degrees (negative = outward-up viewer-left!)
        public double ParamBlush = 0.0;

        // Independent 2nd-order Spring-Damper Physics (with soft-cushioned amplitude & velocity thresholds!)
        private readonly Spring1D _physBackHair1 = new Spring1D(38.0, 5.4, 1.35, 7.5);
        private readonly Spring1D _physBackHair2 = new Spring1D(26.0, 4.4, 1.65, 8.5);
        private readonly Spring1D _physFrontHair = new Spring1D(55.0, 6.2, 1.25, 7.0);
        private readonly Spring1D _physAhoge = new Spring1D(95.0, 5.2, 1.45, 11.0);
        private readonly Spring1D _physEarL = new Spring1D(120.0, 7.5, 1.15, 10.5);
        private readonly Spring1D _physEarR = new Spring1D(120.0, 7.5, 1.15, 10.5);
        private readonly Spring1D _physTail = new Spring1D(34.0, 4.4, 1.60, 8.5);
        private readonly Spring1D _physArmL = new Spring1D(44.0, 6.2, 0.85, 5.5);
        private readonly Spring1D _physArmR = new Spring1D(44.0, 6.2, 0.85, 5.5);
        private readonly Spring1D _physSkirt = new Spring1D(48.0, 5.8, 1.30, 7.5);
        private readonly Spring1D _physBounce = new Spring1D(160.0, 11.0, 1.35, 14.0);
        private readonly Spring1D _physSquash = new Spring1D(130.0, 11.5, 0.22, 3.5);

        private string _state = "idle";
        private double _stateElapsed = 0.0;
        private DateTime _stateExpireAt = DateTime.MaxValue;
        private DateTime _bubbleExpireAt = DateTime.MinValue;
        private DateTime _lastFrameTime = DateTime.UtcNow;
        private double _simTime = 0.0;

        private double _nextBlinkIn = 2.6;
        private double _blinkPhase = -1.0;
        private int _pendingDoubleBlink = 0;
        private double _nextEarTwitchIn = 1.6;
        private int _pendingEarDoubleTwitch = 0;
        private int _lastTwitchSide = 0;
        private double _nextAhogeWiggleIn = 2.2;
        private double _nextAutoActionIn = 22.0;
        private double _ambientParticleCooldown = 0.0;
        private double _idleMouseTimer = 0.0;
        private int _lastCursorX = 0, _lastCursorY = 0;

        private int _headpatStreak = 0;
        private DateTime _lastHeadpatAt = DateTime.MinValue;
        private int _pokeStreak = 0;
        private DateTime _lastPokeAt = DateTime.MinValue;
        private DateTime _lastClickAt = DateTime.MinValue;

        private bool _isDragging = false;
        private Point _dragStartScreen;
        private double _dragLastScreenX, _dragLastScreenY;
        private double _dragStartWinLeft, _dragStartWinTop;
        private bool _movedDuringDrag = false;
        private double _dragVelX = 0.0, _dragVelY = 0.0;
        private double _shakeEnergy = 0.0;

        private double _petScale = 1.0;
        private bool _showBubbles = true;
        private bool _mouseFollow = true;
        private bool _autoActions = true;

        private WinForms.NotifyIcon _trayIcon;
        private IntPtr _trayHIcon = IntPtr.Zero;
        private MenuItem _sizeItemSmall;
        private MenuItem _sizeItemMedium;
        private MenuItem _sizeItemLarge;
        private UdpClient _udpListener;
        private Thread _udpThread;
        private volatile bool _running = true;
        private bool _companionMode = false;
        private readonly HashSet<int> _hostPids = new HashSet<int>();
        private DateTime _hostsEmptySince = DateTime.MaxValue;
        private System.Windows.Threading.DispatcherTimer _watchdogTimer;

        private readonly string[] _headpatLines = new string[]
        {
            "\u55b5\u545c\uff01\u88ab\u4e3b\u4eba\u6478\u5934\u5f00\u5fc3\u5f97\u8e66\u8d77\u6765\u5566 \u2726",
            "\u547c\u565c\u547c\u565c~ \u4e3b\u4eba\u7684\u624b\u597d\u6e29\u6696\uff0c\u518d\u6478\u4e00\u4f1a\u513f\u55b5 \u2661",
            "\u4e3b\u4eba\u6478\u5934\u5145\u80fd\u5b8c\u6210\uff01Gemini \u732b\u5a18\u5143\u6c14\u6ee1\u6ee1\u55b5 \u2726"
        };

        private readonly string[] _pokeLines = new string[]
        {
            "\u4e3b\u4eba\u6233\u6211\u5e72\u561b\u55b5~ \u53cc\u51fb\u770b\u6211\u7ed9\u4e3b\u4eba\u8df3\u5076\u50cf\u821e \u266a",
            "\u4e3b\u4eba\u662f\u5728\u53eb\u6211\u5417\uff1f\u6709\u4ec0\u4e48\u4e8b\u968f\u65f6\u5429\u5490\u55b5 \u2726",
            "\u4e3b\u4eba\u518d\u6233\u6211\u5c31\u8981\u53c9\u8170\u751f\u6c14\u5566\u55b5~"
        };

        public Live2DPetWindow(bool headlessMode, int initialHostPid)
        {
            if (initialHostPid > 0)
            {
                _companionMode = true;
                _hostPids.Add(initialHostPid);
            }
            string exePath = Process.GetCurrentProcess().MainModule.FileName;
            string binDir = System.IO.Path.GetDirectoryName(exePath);
            _baseDir = Directory.GetParent(binDir).FullName;
            _layersDir = System.IO.Path.Combine(_baseDir, "assets\\layers");

            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string cfgDir = System.IO.Path.Combine(localApp, "AntigravityGeminiPet");
            Directory.CreateDirectory(cfgDir);
            _settingsPath = System.IO.Path.Combine(cfgDir, "settings.ini");

            Title = "Antigravity Gemini Live2D Pet";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = !headlessMode;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;

            LoadAllBitmaps();
            BuildVisualTree();

            if (!headlessMode)
            {
                ApplyScale(1.0, false);
                LoadSettings();
                ApplyScale(_petScale, false);
                BuildContextMenu();
                BuildTrayIcon();
                Loaded += OnWindowLoaded;
                Closing += OnWindowClosing;
            }
            else
            {
                ApplyScale(1.0, false);
            }
        }

        private BitmapImage LoadPng(string path)
        {
            BitmapImage bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        private void LoadAllBitmaps()
        {
            string[] files = new string[]
            {
                "00_back_hair", "01_tail", "02_leg-r", "02_leg-l",
                "03_sleeve-r", "04_sleeve-l",
                "05_neck", "06_bottomwear", "07_topwear", "08_face", "09_nose", "10_mouth",
                "11_eyebrow-r", "12_eyebrow-l", "13_eyelash-r", "14_eyelash-l",
                "15_eyewhite-r", "16_eyewhite-l", "17_irides-r", "18_irides-l",
                "19_ears-l", "20_ears-r", "21_front_hair", "22_headwear"
            };
            foreach (string key in files)
            {
                string path = System.IO.Path.Combine(_layersDir, key + ".png");
                if (File.Exists(path)) _layerBitmaps[key] = LoadPng(path);
            }

            string[] handKeys = new string[]
            {
                "hand_r_relaxed", "hand_r_palm", "hand_r_peace", "hand_r_paw",
                "hand_l_relaxed", "hand_l_palm", "hand_l_peace", "hand_l_paw"
            };
            foreach (string hk in handKeys)
            {
                string path = System.IO.Path.Combine(_layersDir, hk + ".png");
                if (File.Exists(path)) _handBitmaps[hk] = LoadPng(path);
            }

            string wandPath = System.IO.Path.Combine(_layersDir, "magic_wand.png");
            if (File.Exists(wandPath)) _wandBitmap = LoadPng(wandPath);
        }

        private Viewport3D CreateOrthoViewport(Model3DGroup group)
        {
            Viewport3D vp = new Viewport3D();
            vp.Width = CanvasSize;
            vp.Height = CanvasSize;
            vp.IsHitTestVisible = false;

            OrthographicCamera cam = new OrthographicCamera();
            cam.Position = new Point3D(CanvasSize * 0.5, -CanvasSize * 0.5, 1000.0);
            cam.LookDirection = new Vector3D(0, 0, -1);
            cam.UpDirection = new Vector3D(0, 1, 0);
            cam.Width = CanvasSize;
            vp.Camera = cam;

            group.Children.Add(new AmbientLight(Colors.White));
            ModelVisual3D mv = new ModelVisual3D();
            mv.Content = group;
            vp.Children.Add(mv);
            return vp;
        }

        private ArtMeshLayer MakeMesh(string key, int gx, int gy, double z)
        {
            if (!_layerBitmaps.ContainsKey(key)) return null;
            ArtMeshLayer layer = new ArtMeshLayer(key, _layerBitmaps[key], gx, gy, z);
            _meshes[key] = layer;
            return layer;
        }

        private void AddMeshTo(Model3DGroup group, string key, int gx, int gy, double z)
        {
            ArtMeshLayer l = MakeMesh(key, gx, gy, z);
            if (l != null) group.Children.Add(l.Model);
        }

        private void BuildVisualTree()
        {
            _outerCanvas = new Canvas();
            _outerCanvas.Width = BaseWinW;
            _outerCanvas.Height = BaseWinH;
            _outerCanvas.Background = Brushes.Transparent;
            Content = _outerCanvas;

            _rigStage = new Grid();
            _rigStage.Width = CanvasSize;
            _rigStage.Height = CanvasSize;
            _rigStage.Background = Brushes.Transparent;
            _rigStage.Cursor = Cursors.Hand;

            double stageScale = 0.56;
            TransformGroup stageTg = new TransformGroup();
            _stageSquashScale = new ScaleTransform(1.0, 1.0, PivotFeetX, PivotFeetY);
            _stageBounceShift = new TranslateTransform(0.0, 0.0);
            stageTg.Children.Add(_stageSquashScale);
            stageTg.Children.Add(_stageBounceShift);
            stageTg.Children.Add(new ScaleTransform(stageScale, stageScale));
            _rigStage.RenderTransform = stageTg;

            Polygon hitPoly = new Polygon();
            hitPoly.Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            hitPoly.Points = new PointCollection(new Point[]
            {
                new Point(310, 25), new Point(460, 25),
                new Point(550, 175), new Point(575, 360),
                new Point(585, 530), new Point(475, 565),
                new Point(470, 760), new Point(265, 760),
                new Point(240, 520), new Point(200, 360),
                new Point(225, 175)
            });
            hitPoly.MouseLeftButtonDown += OnCharMouseDown;
            hitPoly.MouseMove += OnCharMouseMove;
            hitPoly.MouseLeftButtonUp += OnCharMouseUp;

            // LAYER 1A: Back Body (Back Hair, Tail, Independent Right/Left Legs, Behind-Back Arm Slot!)
            _backBodyGroup = new Model3DGroup();
            AddMeshTo(_backBodyGroup, "00_back_hair", 12, 16, 1.0);
            AddMeshTo(_backBodyGroup, "01_tail", 12, 6, 2.0);
            AddMeshTo(_backBodyGroup, "02_leg-r", 8, 18, 3.0);
            AddMeshTo(_backBodyGroup, "02_leg-l", 8, 18, 3.2);
            _rigStage.Children.Add(CreateOrthoViewport(_backBodyGroup));

            // LAYER 1B: Torso & Skirt & Neck (always in front of behind-back arms!)
            Model3DGroup torsoGroup = new Model3DGroup();
            AddMeshTo(torsoGroup, "06_bottomwear", 10, 6, 4.0);
            AddMeshTo(torsoGroup, "07_topwear", 8, 8, 5.0);
            AddMeshTo(torsoGroup, "05_neck", 4, 4, 6.0);
            _rigStage.Children.Add(CreateOrthoViewport(torsoGroup));

            // LAYER 1C: Articulated Swappable Hands + Conjured Magic Wand (Wand sits right behind gripped Right Fist!)
            _handCanvas = new Canvas();
            _handCanvas.Width = CanvasSize;
            _handCanvas.Height = CanvasSize;
            _handCanvas.IsHitTestVisible = false;

            _wandImg = new Image();
            _wandImg.Width = 122.0;
            _wandImg.Height = 264.0;
            _wandImg.Stretch = Stretch.Uniform;
            _wandImg.IsHitTestVisible = false;
            _wandImg.Opacity = 0.0;
            RenderOptions.SetBitmapScalingMode(_wandImg, BitmapScalingMode.HighQuality);
            if (_wandBitmap != null) _wandImg.Source = _wandBitmap;
            TransformGroup wtg = new TransformGroup();
            _wandScale = new ScaleTransform(0.0, 0.0, 61.0, 192.0);
            _wandRot = new RotateTransform(-18.0, 61.0, 192.0);
            _wandShift = new TranslateTransform(0.0, 0.0);
            wtg.Children.Add(_wandScale);
            wtg.Children.Add(_wandRot);
            wtg.Children.Add(_wandShift);
            _wandImg.RenderTransform = wtg;
            _handCanvas.Children.Add(_wandImg);

            _handRImg = MakeHandImage("hand_r_relaxed");
            _handLImg = MakeHandImage("hand_l_relaxed");
            _handRRot = new RotateTransform(0.0, 27.0, 8.0);
            _handLRot = new RotateTransform(0.0, 27.0, 8.0);
            _handRImg.RenderTransform = _handRRot;
            _handLImg.RenderTransform = _handLRot;
            _handCanvas.Children.Add(_handRImg);
            _handCanvas.Children.Add(_handLImg);
            _rigStage.Children.Add(_handCanvas);

            // LAYER 1D: Front Sleeves (12x22 ArtMesh! Dynamically moves to _backBodyGroup when hands go behind back!)
            _frontArmGroup = new Model3DGroup();
            AddMeshTo(_frontArmGroup, "03_sleeve-r", 12, 22, 7.0);
            AddMeshTo(_frontArmGroup, "04_sleeve-l", 12, 22, 7.5);
            _rigStage.Children.Add(CreateOrthoViewport(_frontArmGroup));

            // LAYER 1E: Face Mesh (cleanly above collar/sleeves)
            Model3DGroup faceMeshGroup = new Model3DGroup();
            AddMeshTo(faceMeshGroup, "08_face", 8, 8, 9.0);
            _rigStage.Children.Add(CreateOrthoViewport(faceMeshGroup));

            // LAYER 2: Native Triple-Locked Clipped Facial Rig
            _faceCanvas = new Canvas();
            _faceCanvas.Width = CanvasSize;
            _faceCanvas.Height = CanvasSize;
            _faceCanvas.IsHitTestVisible = false;
            BuildFacialRig();
            _rigStage.Children.Add(_faceCanvas);

            // LAYER 3: Front-of-Face ArtMeshes (Cat Ears, Bangs + Ahoge, Star Hairpin)
            Model3DGroup frontGroup = new Model3DGroup();
            AddMeshTo(frontGroup, "19_ears-l", 6, 6, 10.0);
            AddMeshTo(frontGroup, "20_ears-r", 6, 6, 11.0);
            AddMeshTo(frontGroup, "21_front_hair", 12, 12, 12.0);
            AddMeshTo(frontGroup, "22_headwear", 4, 4, 13.0);
            _rigStage.Children.Add(CreateOrthoViewport(frontGroup));

            // LAYER 4: Magic Working Star Hologram
            BuildMagicHologram();
            _rigStage.Children.Add(_magicOrbGroup);
            _rigStage.Children.Add(hitPoly);

            // Position stage with ~48px extra top jump headroom!
            Canvas.SetLeft(_rigStage, (BaseWinW - CanvasSize * stageScale) * 0.5);
            Canvas.SetTop(_rigStage, BaseWinH - 442.0);
            _outerCanvas.Children.Add(_rigStage);

            // LAYER 5: Particles
            _particleCanvas = new Canvas();
            _particleCanvas.Width = BaseWinW;
            _particleCanvas.Height = BaseWinH;
            _particleCanvas.IsHitTestVisible = false;
            _outerCanvas.Children.Add(_particleCanvas);

            // LAYER 6: Larger, Closer Anime Speech Bubble Window (frosted lavender-pearl + Gemini star accents + seamless speech tail!)
            _bubbleContainer = new StackPanel();
            _bubbleContainer.Orientation = Orientation.Vertical;
            _bubbleContainer.HorizontalAlignment = HorizontalAlignment.Center;
            _bubbleContainer.Opacity = 0.0;
            _bubbleContainer.IsHitTestVisible = false;

            DropShadowEffect bs = new DropShadowEffect();
            bs.Color = Color.FromRgb(92, 68, 168); bs.BlurRadius = 15; bs.ShadowDepth = 3; bs.Opacity = 0.30;
            _bubbleContainer.Effect = bs;

            _bubbleBorder = new Border();
            _bubbleBorder.MaxWidth = 368;
            _bubbleBorder.MinWidth = 178;
            _bubbleBorder.Padding = new Thickness(18, 10, 18, 11);
            _bubbleBorder.CornerRadius = new CornerRadius(20);
            LinearGradientBrush bgBrush = new LinearGradientBrush();
            bgBrush.StartPoint = new Point(0, 0); bgBrush.EndPoint = new Point(1, 1);
            bgBrush.GradientStops.Add(new GradientStop(Color.FromArgb(247, 253, 250, 255), 0.0));
            bgBrush.GradientStops.Add(new GradientStop(Color.FromArgb(245, 242, 234, 255), 0.55));
            bgBrush.GradientStops.Add(new GradientStop(Color.FromArgb(245, 252, 236, 252), 1.0));
            _bubbleBorder.Background = bgBrush;

            LinearGradientBrush borderGrad = new LinearGradientBrush();
            borderGrad.StartPoint = new Point(0, 0); borderGrad.EndPoint = new Point(1, 1);
            borderGrad.GradientStops.Add(new GradientStop(Color.FromArgb(245, 152, 128, 246), 0.0));
            borderGrad.GradientStops.Add(new GradientStop(Color.FromArgb(245, 122, 208, 252), 0.52));
            borderGrad.GradientStops.Add(new GradientStop(Color.FromArgb(245, 238, 156, 224), 1.0));
            _bubbleBorder.BorderBrush = borderGrad;
            _bubbleBorder.BorderThickness = new Thickness(2.0);

            Grid bubbleInner = new Grid();
            TextBlock starTL = new TextBlock();
            starTL.Text = "\u2726";
            starTL.FontSize = 10.5;
            starTL.Foreground = new SolidColorBrush(Color.FromRgb(102, 194, 248));
            starTL.HorizontalAlignment = HorizontalAlignment.Left;
            starTL.VerticalAlignment = VerticalAlignment.Top;
            starTL.Margin = new Thickness(-10, -6, 0, 0);
            bubbleInner.Children.Add(starTL);

            TextBlock starBR = new TextBlock();
            starBR.Text = "\u2727";
            starBR.FontSize = 10.5;
            starBR.Foreground = new SolidColorBrush(Color.FromRgb(234, 134, 206));
            starBR.HorizontalAlignment = HorizontalAlignment.Right;
            starBR.VerticalAlignment = VerticalAlignment.Bottom;
            starBR.Margin = new Thickness(0, 0, -10, -6);
            bubbleInner.Children.Add(starBR);

            _bubbleText = new TextBlock();
            _bubbleText.Foreground = new SolidColorBrush(Color.FromRgb(56, 40, 102));
            _bubbleText.FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
            _bubbleText.FontSize = 14.5;
            _bubbleText.FontWeight = FontWeights.SemiBold;
            _bubbleText.LineHeight = 21.0;
            _bubbleText.TextWrapping = TextWrapping.Wrap;
            _bubbleText.TextAlignment = TextAlignment.Center;
            _bubbleText.Margin = new Thickness(4, 0, 4, 0);
            TextOptions.SetTextFormattingMode(_bubbleText, TextFormattingMode.Display);
            bubbleInner.Children.Add(_bubbleText);
            _bubbleBorder.Child = bubbleInner;
            _bubbleContainer.Children.Add(_bubbleBorder);

            // Seamless pointed anime comic speech tail attached cleanly to bottom of bubble!
            Canvas tailHost = new Canvas();
            tailHost.Width = 28;
            tailHost.Height = 18;
            tailHost.HorizontalAlignment = HorizontalAlignment.Center;
            tailHost.Margin = new Thickness(0, -2.5, 0, 0);

            System.Windows.Shapes.Path tailFill = new System.Windows.Shapes.Path();
            tailFill.Fill = new SolidColorBrush(Color.FromArgb(246, 246, 236, 254));
            StreamGeometry tfg = new StreamGeometry();
            using (StreamGeometryContext tfc = tfg.Open())
            {
                tfc.BeginFigure(new Point(2, 0), true, true);
                tfc.LineTo(new Point(25, 0), false, false);
                tfc.QuadraticBezierTo(new Point(17, 10), new Point(11, 17), true, true);
                tfc.QuadraticBezierTo(new Point(9, 8), new Point(2, 0), true, true);
            }
            tfg.Freeze();
            tailFill.Data = tfg;
            tailHost.Children.Add(tailFill);

            System.Windows.Shapes.Path tailStroke = new System.Windows.Shapes.Path();
            tailStroke.Stroke = new SolidColorBrush(Color.FromArgb(245, 146, 172, 246));
            tailStroke.StrokeThickness = 2.0;
            tailStroke.StrokeLineJoin = PenLineJoin.Round;
            tailStroke.StrokeStartLineCap = PenLineCap.Round;
            tailStroke.StrokeEndLineCap = PenLineCap.Round;
            StreamGeometry tsg = new StreamGeometry();
            using (StreamGeometryContext tsc = tsg.Open())
            {
                tsc.BeginFigure(new Point(2.5, 0.8), false, false);
                tsc.QuadraticBezierTo(new Point(9, 8.2), new Point(11, 17), true, true);
                tsc.QuadraticBezierTo(new Point(17, 10), new Point(24.5, 0.8), true, true);
            }
            tsg.Freeze();
            tailStroke.Data = tsg;
            tailHost.Children.Add(tailStroke);
            _bubbleContainer.Children.Add(tailHost);

            _bubbleHost = new Grid();
            _bubbleHost.Width = BaseWinW;
            _bubbleHost.IsHitTestVisible = false;
            _bubbleHost.Children.Add(_bubbleContainer);
            Canvas.SetTop(_bubbleHost, 56);
            Canvas.SetLeft(_bubbleHost, 0);
            _outerCanvas.Children.Add(_bubbleHost);
        }

        private Image MakeHandImage(string key)
        {
            Image img = new Image();
            img.Width = 54.0;
            img.Height = 54.0;
            img.Stretch = Stretch.Uniform;
            img.IsHitTestVisible = false;
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            if (_handBitmaps.ContainsKey(key)) img.Source = _handBitmaps[key];
            return img;
        }

        private void SetArmsBehindBack(bool behind)
        {
            if (_armsBehindBack == behind) return;
            _armsBehindBack = behind;
            if (!_meshes.ContainsKey("03_sleeve-r") || !_meshes.ContainsKey("04_sleeve-l")) return;

            ArtMeshLayer sr = _meshes["03_sleeve-r"];
            ArtMeshLayer sl = _meshes["04_sleeve-l"];
            if (behind)
            {
                _frontArmGroup.Children.Remove(sr.Model);
                _frontArmGroup.Children.Remove(sl.Model);
                sr.ZDepth = 2.5; sl.ZDepth = 2.6;
                if (!_backBodyGroup.Children.Contains(sr.Model)) _backBodyGroup.Children.Add(sr.Model);
                if (!_backBodyGroup.Children.Contains(sl.Model)) _backBodyGroup.Children.Add(sl.Model);
                _handCanvas.Opacity = 0.0; // Hands neatly hidden behind her back!
            }
            else
            {
                _backBodyGroup.Children.Remove(sr.Model);
                _backBodyGroup.Children.Remove(sl.Model);
                sr.ZDepth = 7.0; sl.ZDepth = 7.5;
                if (!_frontArmGroup.Children.Contains(sr.Model)) _frontArmGroup.Children.Add(sr.Model);
                if (!_frontArmGroup.Children.Contains(sl.Model)) _frontArmGroup.Children.Add(sl.Model);
                _handCanvas.Opacity = 1.0;
            }
        }

        private void UpdateHandModel(Image img, ref string currentPose, string desiredPose, string sidePrefix)
        {
            if (currentPose == desiredPose) return;
            string key = sidePrefix + "_" + desiredPose;
            if (_handBitmaps.ContainsKey(key))
            {
                img.Source = _handBitmaps[key];
                currentPose = desiredPose;
            }
        }

        private Image MakeFullFaceImage(string key)
        {
            Image img = new Image();
            img.Width = CanvasSize;
            img.Height = CanvasSize;
            img.Stretch = Stretch.Fill;
            img.IsHitTestVisible = false;
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            if (_layerBitmaps.ContainsKey(key)) img.Source = _layerBitmaps[key];
            return img;
        }

        private void BuildClippedEye(
            string whiteKey, string irisKey, string lashKey,
            double cx, double cy, double clipRx, double clipRy,
            out Grid eyeRoot, out Grid maskGroup,
            out ScaleTransform openScale, out TranslateTransform irisShift, out ScaleTransform irisScale,
            out Image lashImg, out TranslateTransform lashShift, out ScaleTransform lashScale,
            out System.Windows.Shapes.Path smileLash, out System.Windows.Shapes.Path sleepLash)
        {
            eyeRoot = new Grid(); eyeRoot.Width = CanvasSize; eyeRoot.Height = CanvasSize; eyeRoot.IsHitTestVisible = false;
            maskGroup = new Grid(); maskGroup.Width = CanvasSize; maskGroup.Height = CanvasSize;
            openScale = new ScaleTransform(1.0, 1.0, cx, cy + 5.0);
            maskGroup.RenderTransform = openScale;
            maskGroup.Children.Add(MakeFullFaceImage(whiteKey));

            Grid irisClipper = new Grid();
            irisClipper.Width = CanvasSize; irisClipper.Height = CanvasSize;
            EllipseGeometry hardClip = new EllipseGeometry(new Point(cx, cy + 0.5), clipRx, clipRy);
            hardClip.Freeze();
            irisClipper.Clip = hardClip;

            if (_layerBitmaps.ContainsKey(whiteKey))
            {
                ImageBrush maskBrush = new ImageBrush(_layerBitmaps[whiteKey]);
                maskBrush.Stretch = Stretch.Fill;
                maskBrush.ViewportUnits = BrushMappingMode.RelativeToBoundingBox;
                maskBrush.Freeze();
                irisClipper.OpacityMask = maskBrush;
            }

            Image irisImg = MakeFullFaceImage(irisKey);
            TransformGroup itg = new TransformGroup();
            irisScale = new ScaleTransform(1.0, 1.0, cx, cy);
            irisShift = new TranslateTransform(0.0, 0.0);
            itg.Children.Add(irisScale); itg.Children.Add(irisShift);
            irisImg.RenderTransform = itg;
            irisClipper.Children.Add(irisImg);

            maskGroup.Children.Add(irisClipper);
            eyeRoot.Children.Add(maskGroup);

            lashImg = MakeFullFaceImage(lashKey);
            TransformGroup ltg = new TransformGroup();
            lashScale = new ScaleTransform(1.0, 1.0, cx, cy - 6.0);
            lashShift = new TranslateTransform(0.0, 0.0);
            ltg.Children.Add(lashScale); ltg.Children.Add(lashShift);
            lashImg.RenderTransform = ltg;
            eyeRoot.Children.Add(lashImg);

            smileLash = new System.Windows.Shapes.Path();
            smileLash.Stroke = new SolidColorBrush(Color.FromRgb(82, 44, 70));
            smileLash.StrokeThickness = 3.2;
            smileLash.StrokeStartLineCap = PenLineCap.Round;
            smileLash.StrokeEndLineCap = PenLineCap.Round;
            smileLash.Opacity = 0.0;
            StreamGeometry sg = new StreamGeometry();
            using (StreamGeometryContext sgc = sg.Open())
            {
                double xL = cx - 12.0, xR = cx + 12.0, yBase = cy + 2.5;
                sgc.BeginFigure(new Point(xL, yBase), false, false);
                sgc.QuadraticBezierTo(new Point(cx, yBase - 11.0), new Point(xR, yBase), true, true);
            }
            sg.Freeze();
            smileLash.Data = sg;
            eyeRoot.Children.Add(smileLash);

            // Peaceful downward-curved closed sleeping eyelash (u u) matching anime art!
            sleepLash = new System.Windows.Shapes.Path();
            sleepLash.Stroke = new SolidColorBrush(Color.FromRgb(82, 44, 70));
            sleepLash.StrokeThickness = 2.8;
            sleepLash.StrokeStartLineCap = PenLineCap.Round;
            sleepLash.StrokeEndLineCap = PenLineCap.Round;
            sleepLash.Opacity = 0.0;
            StreamGeometry slg = new StreamGeometry();
            using (StreamGeometryContext sgc = slg.Open())
            {
                double xL = cx - 11.5, xR = cx + 11.5, yMid = cy + 2.5;
                sgc.BeginFigure(new Point(xL, yMid - 1.5), false, false);
                sgc.QuadraticBezierTo(new Point(cx, yMid + 5.8), new Point(xR, yMid - 1.5), true, true);
            }
            slg.Freeze();
            sleepLash.Data = slg;
            eyeRoot.Children.Add(sleepLash);
        }

        private void BuildFacialRig()
        {
            _blushR = CreateBlushEllipse(344, 145, 28, 14);
            _blushL = CreateBlushEllipse(396, 145, 28, 14);
            _faceCanvas.Children.Add(_blushR);
            _faceCanvas.Children.Add(_blushL);

            _noseImg = MakeFullFaceImage("09_nose");
            _faceCanvas.Children.Add(_noseImg);
            _mouthImg = MakeFullFaceImage("10_mouth");
            _faceCanvas.Children.Add(_mouthImg);

            _happyMouthOverlay = new Canvas();
            _happyMouthOverlay.Width = 24; _happyMouthOverlay.Height = 16; _happyMouthOverlay.Opacity = 0.0;
            Ellipse skinPatch = new Ellipse();
            skinPatch.Width = 22; skinPatch.Height = 12;
            skinPatch.Fill = new SolidColorBrush(Color.FromRgb(255, 237, 233));
            Canvas.SetLeft(skinPatch, 1); Canvas.SetTop(skinPatch, 1);
            _happyMouthOverlay.Children.Add(skinPatch);

            System.Windows.Shapes.Path openSmile = new System.Windows.Shapes.Path();
            openSmile.Fill = new SolidColorBrush(Color.FromRgb(235, 118, 142));
            openSmile.Stroke = new SolidColorBrush(Color.FromRgb(120, 62, 76));
            openSmile.StrokeThickness = 1.4;
            StreamGeometry mg = new StreamGeometry();
            using (StreamGeometryContext mc = mg.Open())
            {
                mc.BeginFigure(new Point(4, 5), true, true);
                mc.QuadraticBezierTo(new Point(12, 4), new Point(20, 5), true, true);
                mc.QuadraticBezierTo(new Point(12, 16), new Point(4, 5), true, true);
            }
            mg.Freeze();
            openSmile.Data = mg;
            _happyMouthOverlay.Children.Add(openSmile);

            Polygon fang = new Polygon();
            fang.Fill = Brushes.White;
            fang.Points = new PointCollection(new Point[] { new Point(14.5, 5.2), new Point(17.2, 5.2), new Point(16.0, 8.2) });
            _happyMouthOverlay.Children.Add(fang);
            Canvas.SetLeft(_happyMouthOverlay, 371); Canvas.SetTop(_happyMouthOverlay, 157);
            _faceCanvas.Children.Add(_happyMouthOverlay);

            _surprisedMouthOverlay = new Canvas();
            _surprisedMouthOverlay.Width = 24; _surprisedMouthOverlay.Height = 16; _surprisedMouthOverlay.Opacity = 0.0;
            Ellipse sp2 = new Ellipse();
            sp2.Width = 22; sp2.Height = 12; sp2.Fill = new SolidColorBrush(Color.FromRgb(255, 237, 233));
            Canvas.SetLeft(sp2, 1); Canvas.SetTop(sp2, 1);
            _surprisedMouthOverlay.Children.Add(sp2);
            Ellipse oMouth = new Ellipse();
            oMouth.Width = 10; oMouth.Height = 11;
            oMouth.Fill = new SolidColorBrush(Color.FromRgb(232, 114, 140));
            oMouth.Stroke = new SolidColorBrush(Color.FromRgb(118, 60, 76));
            oMouth.StrokeThickness = 1.4;
            Canvas.SetLeft(oMouth, 7); Canvas.SetTop(oMouth, 3);
            _surprisedMouthOverlay.Children.Add(oMouth);
            Canvas.SetLeft(_surprisedMouthOverlay, 371); Canvas.SetTop(_surprisedMouthOverlay, 157);
            _faceCanvas.Children.Add(_surprisedMouthOverlay);

            _poutMouthOverlay = new Canvas();
            _poutMouthOverlay.Width = 24; _poutMouthOverlay.Height = 16; _poutMouthOverlay.Opacity = 0.0;
            Ellipse sp3 = new Ellipse();
            sp3.Width = 22; sp3.Height = 12; sp3.Fill = new SolidColorBrush(Color.FromRgb(255, 237, 233));
            Canvas.SetLeft(sp3, 1); Canvas.SetTop(sp3, 1);
            _poutMouthOverlay.Children.Add(sp3);
            System.Windows.Shapes.Path poutArc = new System.Windows.Shapes.Path();
            poutArc.Stroke = new SolidColorBrush(Color.FromRgb(118, 62, 78));
            poutArc.StrokeThickness = 1.8;
            poutArc.StrokeStartLineCap = PenLineCap.Round; poutArc.StrokeEndLineCap = PenLineCap.Round;
            StreamGeometry pg = new StreamGeometry();
            using (StreamGeometryContext pc = pg.Open())
            {
                pc.BeginFigure(new Point(6, 9), false, false);
                pc.QuadraticBezierTo(new Point(12, 5), new Point(18, 9), true, true);
            }
            pg.Freeze();
            poutArc.Data = pg;
            _poutMouthOverlay.Children.Add(poutArc);
            Canvas.SetLeft(_poutMouthOverlay, 371); Canvas.SetTop(_poutMouthOverlay, 157);
            _faceCanvas.Children.Add(_poutMouthOverlay);

            _thinkMouthOverlay = new Ellipse();
            _thinkMouthOverlay.Width = 6.5; _thinkMouthOverlay.Height = 5.5;
            _thinkMouthOverlay.Fill = new SolidColorBrush(Color.FromRgb(230, 130, 150));
            _thinkMouthOverlay.Stroke = new SolidColorBrush(Color.FromRgb(125, 70, 84));
            _thinkMouthOverlay.StrokeThickness = 1.1; _thinkMouthOverlay.Opacity = 0.0;
            Canvas.SetLeft(_thinkMouthOverlay, 380); Canvas.SetTop(_thinkMouthOverlay, 161);
            _faceCanvas.Children.Add(_thinkMouthOverlay);

            BuildClippedEye("15_eyewhite-r", "17_irides-r", "13_eyelash-r", EyeRCX, EyeRCY, 10.0, 7.2,
                out _eyeRRoot, out _eyeRMaskGroup, out _eyeROpenScale, out _eyeRIrisShift, out _eyeRIrisScale,
                out _eyelashRImg, out _eyelashRShift, out _eyelashRScale, out _smileLashR, out _sleepLashR);
            BuildClippedEye("16_eyewhite-l", "18_irides-l", "14_eyelash-l", EyeLCX, EyeLCY, 9.8, 7.2,
                out _eyeLRoot, out _eyeLMaskGroup, out _eyeLOpenScale, out _eyeLIrisShift, out _eyeLIrisScale,
                out _eyelashLImg, out _eyelashLShift, out _eyelashLScale, out _smileLashL, out _sleepLashL);

            _faceCanvas.Children.Add(_eyeRRoot);
            _faceCanvas.Children.Add(_eyeLRoot);

            _browRImg = MakeFullFaceImage("11_eyebrow-r");
            _browLImg = MakeFullFaceImage("12_eyebrow-l");
            _faceCanvas.Children.Add(_browRImg);
            _faceCanvas.Children.Add(_browLImg);
        }

        private Ellipse CreateBlushEllipse(double left, double top, double w, double h)
        {
            Ellipse el = new Ellipse();
            el.Width = w; el.Height = h;
            RadialGradientBrush rb = new RadialGradientBrush();
            rb.GradientStops.Add(new GradientStop(Color.FromArgb(165, 255, 108, 156), 0.0));
            rb.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 108, 156), 1.0));
            el.Fill = rb; el.Opacity = 0.0;
            Canvas.SetLeft(el, left); Canvas.SetTop(el, top);
            return el;
        }

        private void BuildMagicHologram()
        {
            _magicOrbGroup = new Canvas();
            _magicOrbGroup.Width = CanvasSize; _magicOrbGroup.Height = CanvasSize;
            _magicOrbGroup.IsHitTestVisible = false;

            // 1. Wand-Tip Radiant Gemini Magic Circle + Twinkling Halo (tracks moving wand tip!)
            _wandTipNode = new Canvas();
            _wandTipNode.Width = 144; _wandTipNode.Height = 144; _wandTipNode.Opacity = 0.0;
            TransformGroup tg = new TransformGroup();
            _magicOrbScale = new ScaleTransform(1.0, 1.0, 72, 72);
            _magicOrbRot = new RotateTransform(0.0, 72, 72);
            tg.Children.Add(_magicOrbScale); tg.Children.Add(_magicOrbRot);
            _wandTipNode.RenderTransform = tg;

            Ellipse glow = new Ellipse();
            glow.Width = 144; glow.Height = 144;
            RadialGradientBrush gb = new RadialGradientBrush();
            gb.GradientStops.Add(new GradientStop(Color.FromArgb(185, 232, 248, 255), 0.0));
            gb.GradientStops.Add(new GradientStop(Color.FromArgb(125, 166, 224, 255), 0.36));
            gb.GradientStops.Add(new GradientStop(Color.FromArgb(75, 198, 148, 255), 0.68));
            gb.GradientStops.Add(new GradientStop(Color.FromArgb(0, 160, 120, 255), 1.0));
            glow.Fill = gb;
            _wandTipNode.Children.Add(glow);

            Ellipse outerRing = new Ellipse();
            outerRing.Width = 114; outerRing.Height = 114;
            outerRing.Stroke = new SolidColorBrush(Color.FromArgb(205, 198, 238, 255));
            outerRing.StrokeThickness = 2.0;
            outerRing.StrokeDashArray = new DoubleCollection(new double[] { 6, 4 });
            Canvas.SetLeft(outerRing, 15); Canvas.SetTop(outerRing, 15);
            _wandTipNode.Children.Add(outerRing);

            Ellipse innerRing = new Ellipse();
            innerRing.Width = 88; innerRing.Height = 88;
            innerRing.Stroke = new SolidColorBrush(Color.FromArgb(190, 245, 206, 255));
            innerRing.StrokeThickness = 1.6;
            Canvas.SetLeft(innerRing, 28); Canvas.SetTop(innerRing, 28);
            _wandTipNode.Children.Add(innerRing);

            // Delicate rotated magic diamond square inside the circle
            Polygon runeDiamond = new Polygon();
            runeDiamond.Points = new PointCollection(new Point[]
            {
                new Point(72, 20), new Point(124, 72), new Point(72, 124), new Point(20, 72)
            });
            runeDiamond.Stroke = new SolidColorBrush(Color.FromArgb(170, 255, 244, 212));
            runeDiamond.StrokeThickness = 1.4;
            _wandTipNode.Children.Add(runeDiamond);

            // Four tiny orbital star points on the magic ring
            double[] sxPts = new double[] { 72, 129, 72, 15 };
            double[] syPts = new double[] { 15, 72, 129, 72 };
            for (int i = 0; i < 4; i++)
            {
                Ellipse dot = new Ellipse();
                dot.Width = 7; dot.Height = 7;
                dot.Fill = new SolidColorBrush((i % 2 == 0) ? Color.FromArgb(245, 255, 250, 224) : Color.FromArgb(245, 174, 236, 255));
                Canvas.SetLeft(dot, sxPts[i] - 3.5); Canvas.SetTop(dot, syPts[i] - 3.5);
                _wandTipNode.Children.Add(dot);
            }
            _magicOrbGroup.Children.Add(_wandTipNode);

            // 2. Conjure Summoning Flash Burst (blooms right around her hand as the wand appears out of thin air!)
            _summonFlashNode = new Canvas();
            _summonFlashNode.Width = 170; _summonFlashNode.Height = 170; _summonFlashNode.Opacity = 0.0;
            TransformGroup stg = new TransformGroup();
            _summonFlashScale = new ScaleTransform(0.2, 0.2, 85, 85);
            _summonFlashRot = new RotateTransform(0.0, 85, 85);
            stg.Children.Add(_summonFlashScale); stg.Children.Add(_summonFlashRot);
            _summonFlashNode.RenderTransform = stg;

            Ellipse flashAura = new Ellipse();
            flashAura.Width = 170; flashAura.Height = 170;
            RadialGradientBrush fab = new RadialGradientBrush();
            fab.GradientStops.Add(new GradientStop(Color.FromArgb(240, 255, 255, 255), 0.0));
            fab.GradientStops.Add(new GradientStop(Color.FromArgb(195, 174, 236, 255), 0.35));
            fab.GradientStops.Add(new GradientStop(Color.FromArgb(120, 212, 162, 255), 0.68));
            fab.GradientStops.Add(new GradientStop(Color.FromArgb(0, 190, 140, 255), 1.0));
            flashAura.Fill = fab;
            _summonFlashNode.Children.Add(flashAura);

            Ellipse flashRing = new Ellipse();
            flashRing.Width = 140; flashRing.Height = 140;
            flashRing.Stroke = new SolidColorBrush(Color.FromArgb(235, 255, 246, 212));
            flashRing.StrokeThickness = 3.0;
            flashRing.StrokeDashArray = new DoubleCollection(new double[] { 4, 3 });
            Canvas.SetLeft(flashRing, 15); Canvas.SetTop(flashRing, 15);
            _summonFlashNode.Children.Add(flashRing);

            Polygon burstStar = new Polygon();
            burstStar.Points = new PointCollection(new Point[]
            {
                new Point(85, 6), new Point(98, 72), new Point(164, 85), new Point(98, 98),
                new Point(85, 164), new Point(72, 98), new Point(6, 85), new Point(72, 72)
            });
            burstStar.Fill = new SolidColorBrush(Color.FromArgb(225, 255, 255, 255));
            _summonFlashNode.Children.Add(burstStar);

            _magicOrbGroup.Children.Add(_summonFlashNode);
        }

        private static double SmoothStep(double edge0, double edge1, double x)
        {
            double u = Math.Max(0.0, Math.Min(1.0, (x - edge0) / (edge1 - edge0)));
            return u * u * (3.0 - 2.0 * u);
        }

        private static Point RotatePoint(double px, double py, double cx, double cy, double rad)
        {
            double dx = px - cx, dy = py - cy;
            double c = Math.Cos(rad), s = Math.Sin(rad);
            return new Point(cx + dx * c - dy * s, cy + dx * s + dy * c);
        }

        // Deforms 2-bone sleeve AND positions/rotates the independent swappable Hand Model at the exact moved Wrist Cuff!
        // Returns the exact grip center point inside the hand (for attaching props like the conjured Magic Wand!).
        private Point DeformSleeveAndHand(
            string sleeveKey, Image handImg,
            double sx, double sy, double ex, double ey, double wx, double wy,
            double upperRad, double elbowRad, double wristRad,
            bool isLeftArm, bool hipLocked, double shiftX, double breathLift, double t)
        {
            double sign = isLeftArm ? -1.0 : 1.0;
            double idleWave = hipLocked ? 0.0 : (Math.Sin(t * 2.1 + sign) * 0.014);
            double a1 = sign * (upperRad + idleWave);
            double a2 = sign * elbowRad;
            double a3 = sign * wristRad;

            Point elbowMoved = RotatePoint(ex, ey, sx, sy, a1);
            double c12 = Math.Cos(a1 + a2), s12 = Math.Sin(a1 + a2);
            Point wristMoved = new Point(
                elbowMoved.X + (wx - ex) * c12 - (wy - ey) * s12,
                elbowMoved.Y + (wx - ex) * s12 + (wy - ey) * c12);

            double fdx = wx - ex, fdy = wy - ey;
            double fLen = Math.Sqrt(fdx * fdx + fdy * fdy);
            double fx = fdx / fLen, fy = fdy / fLen;

            double halfBend = Math.Min(1.05, Math.Abs(a2) * 0.5);
            double invCos = 1.0 / Math.Max(0.72, Math.Cos(halfBend));

            if (_meshes.ContainsKey(sleeveKey))
            {
                _meshes[sleeveKey].Deform(delegate(double x, double y)
                {
                    double projFore = (x - ex) * fx + (y - ey) * fy;
                    double wAnchor = SmoothStep(206.0, 244.0, y);
                    double wElbow = SmoothStep(-26.0, 28.0, projFore);

                    Point p0 = new Point(x, y);
                    Point p1 = RotatePoint(x, y, sx, sy, a1);
                    double relEx = x - ex, relEy = y - ey;
                    Point p2 = new Point(
                        elbowMoved.X + relEx * c12 - relEy * s12,
                        elbowMoved.Y + relEx * s12 + relEy * c12);

                    double bx = p0.X + (p1.X - p0.X) * wAnchor;
                    double by = p0.Y + (p1.Y - p0.Y) * wAnchor;

                    if (wElbow > 0.0001)
                    {
                        double exBlendX = bx + (p2.X - bx) * wElbow;
                        double exBlendY = by + (p2.Y - by) * wElbow;
                        double midW = 4.0 * wElbow * (1.0 - wElbow);
                        if (midW > 0.01 && Math.Abs(a2) > 0.25)
                        {
                            double boost = 1.0 + (invCos - 1.0) * midW * 0.55;
                            exBlendX = elbowMoved.X + (exBlendX - elbowMoved.X) * boost;
                            exBlendY = elbowMoved.Y + (exBlendY - elbowMoved.Y) * boost;
                        }
                        bx = exBlendX;
                        by = exBlendY;
                    }
                    return new Point(bx + shiftX, by + breathLift * 0.78);
                });
            }

            // Place independent swappable Hand Model seamlessly inside moved purple wrist cuff!
            // Local hand image is 54x54 with wrist pivot at (27, 8) and fingers pointing straight down (+Y).
            double restAng = isLeftArm ? -RestForearmRad : RestForearmRad;
            double totalHandRad = restAng + a1 + a2 + a3;
            double totalHandDeg = totalHandRad * 180.0 / Math.PI;
            double wxScreen = wristMoved.X + shiftX;
            double wyScreen = wristMoved.Y + breathLift * 0.78;

            Canvas.SetLeft(handImg, wxScreen - 27.0);
            Canvas.SetTop(handImg, wyScreen - 8.0);
            RotateTransform rot = isLeftArm ? _handLRot : _handRRot;
            if (rot != null) rot.Angle = totalHandDeg;

            // Grip palm center lies ~16px past wrist along the rotated +Y hand axis!
            double gx = wxScreen - Math.Sin(totalHandRad) * 16.0;
            double gy = wyScreen + Math.Cos(totalHandRad) * 16.0;
            return new Point(gx, gy);
        }

        // Anatomically Accurate 2-Bone Articulated Leg:
        // - Grounded Squat / Landing Cushion (feet stay planted on floor, knees bow slightly out & compress smoothly!)
        // - Airborne Backward-Outward Knee Flexion (thigh lifts & foreshortens, shin folds backward-upward with 3D perspective compression!)
        private void DeformLeg(
            string legKey, double hx, double hy, double kx, double ky,
            double spreadRad, double kneeFoldRad, double bendDepth, double squat,
            bool isLeftLeg, double shiftX, double breathLift)
        {
            if (!_meshes.ContainsKey(legKey)) return;
            double sign = isLeftLeg ? -1.0 : 1.0;
            double outDir = isLeftLeg ? 1.0 : -1.0; // viewer outward X direction

            double a1 = sign * spreadRad;
            double a2 = sign * kneeFoldRad;
            double bend = Math.Max(0.0, Math.Min(1.0, bendDepth));
            double sq = Math.Max(0.0, Math.Min(1.0, squat));

            // Hip drops down smoothly with upper body during grounded squat!
            double squatDrop = sq * 26.0;
            // Knee lifts & foreshortens slightly when bending backward in mid-air!
            double thighScaleY = 1.0 - 0.12 * bend;
            Point kneeBase = new Point(kx, hy + (ky - hy) * thighScaleY);
            Point kneeMoved = RotatePoint(kneeBase.X, kneeBase.Y, hx, hy, a1);
            double c12 = Math.Cos(a1 + a2), s12 = Math.Sin(a1 + a2);

            // 3D depth perspective foreshortening along lower leg when bent backward in mid-air!
            double shinScaleY = 1.0 - 0.36 * bend;
            double halfFold = Math.Min(0.85, Math.Abs(a2) * 0.5);
            double kneeVolBoost = 1.0 / Math.Max(0.78, Math.Cos(halfFold));

            _meshes[legKey].Deform(delegate(double x, double y)
            {
                // 1. Grounded Squat / Landing Cushion (feet stay pinned at floor y=755!)
                double sqDx = 0.0, sqDy = 0.0;
                if (sq > 0.001)
                {
                    // Hip drops full squatDrop, fades smoothly to 0 at ankle/shoe (y=720..755)!
                    double hFrac = 1.0 - SmoothStep(405.0, 725.0, y);
                    sqDy = squatDrop * hFrac;
                    // Knees bow gently outward (peaks smoothly at knee y=558, zero at hip & shoe!)
                    double kneeBell = Math.Exp(-Math.Pow((y - ky) / 92.0, 2.0));
                    sqDx = outDir * (13.0 * sq * kneeBell);
                }

                // 2. Airborne Hip Spread + Backward-Outward Knee Flexion
                double wHip = SmoothStep(392.0, 440.0, y);
                double wKnee = SmoothStep(ky - 24.0, ky + 28.0, y);

                Point p0 = new Point(x, y);
                // Thigh foreshortening + hip rotation
                double ty = hy + (y - hy) * thighScaleY;
                Point p1 = RotatePoint(x, ty, hx, hy, a1);

                // Shin folds backward-outward around moved knee with 3D foreshortening!
                double relKx = x - kx;
                double relKy = (y - ky) * shinScaleY;
                // Subtle pointed-toe taper at shoe tip when airborne!
                if (bend > 0.05 && y > 690.0)
                {
                    double toeW = SmoothStep(690.0, 745.0, y);
                    relKy += toeW * bend * 6.0;
                }
                Point p2 = new Point(
                    kneeMoved.X + relKx * c12 - relKy * s12,
                    kneeMoved.Y + relKx * s12 + relKy * c12);

                double bx = p0.X + (p1.X - p0.X) * wHip;
                double by = p0.Y + (p1.Y - p0.Y) * wHip;
                if (wKnee > 0.0001)
                {
                    double kbX = bx + (p2.X - bx) * wKnee;
                    double kbY = by + (p2.Y - by) * wKnee;
                    double midW = 4.0 * wKnee * (1.0 - wKnee);
                    if (midW > 0.01 && Math.Abs(a2) > 0.2)
                    {
                        double boost = 1.0 + (kneeVolBoost - 1.0) * midW * 0.45;
                        kbX = kneeMoved.X + (kbX - kneeMoved.X) * boost;
                        kbY = kneeMoved.Y + (kbY - kneeMoved.Y) * boost;
                    }
                    bx = kbX;
                    by = kbY;
                }

                // Only apply breathLift to upper leg when not squatted
                double legBreath = breathLift * (1.0 - SmoothStep(410.0, 720.0, y)) * 0.35;
                return new Point(bx + sqDx + shiftX * 0.65, by + sqDy + legBreath);
            });
        }

        public void ApplyRigDeformation(double t)
        {
            // Dynamic Z-order swap for arms behind back (Shy!) vs in front of torso!
            SetArmsBehindBack(ParamArmsBehind);
            UpdateHandModel(_handRImg, ref _curHandR, ParamHandR, "hand_r");
            UpdateHandModel(_handLImg, ref _curHandL, ParamHandL, "hand_l");

            double ax = ParamAngleX, ay = ParamAngleY, az = ParamAngleZ;
            double bx = ParamBodyAngleX, bz = ParamBodyAngleZ, shiftX = ParamBodyShiftX;
            double br = ParamBreath, st = ParamStretch;

            double headRad = az * 0.16;
            double bodyRad = bz * 0.068;
            double cosH = Math.Cos(headRad), sinH = Math.Sin(headRad);
            double cosB = Math.Cos(bodyRad), sinB = Math.Sin(bodyRad);

            double pureBreathLift = -br * 5.2 - st * 14.0;
            double breathLift = pureBreathLift + ParamLegSquat * 26.0;
            double headBaseDx = shiftX + bx * 6.5 + bz * 11.5;
            double headBaseDy = breathLift + Math.Abs(ax) * 1.4;

            Func<double, double, double, Point> headTransform = delegate(double x, double y, double depthZ)
            {
                double rx = x - PivotHeadX, ry = y - PivotHeadY;
                double px = rx * (1.0 - Math.Abs(ax) * 0.035) + ax * depthZ * 10.5;
                double py = ry * (1.0 - Math.Abs(ay) * 0.025) + ay * depthZ * 7.5;
                double fx = px * cosH - py * sinH;
                double fy = px * sinH + py * cosH;
                return new Point(PivotHeadX + fx + headBaseDx, PivotHeadY + fy + headBaseDy);
            };

            // Airborne jump billows hair & skirt softly downward!
            double jumpTrail = ParamJumpY * 0.45;

            // 1. Back hair (soft-knee tanh saturation ensures lush sway that NEVER tears off her head!)
            if (_meshes.ContainsKey("00_back_hair"))
            {
                double h1 = 1.35 * Math.Tanh(_physBackHair1.Value / 1.35);
                double h2 = 1.65 * Math.Tanh(_physBackHair2.Value / 1.65);
                _meshes["00_back_hair"].Deform(delegate(double x, double y)
                {
                    Point hp = headTransform(x, y, -0.52);
                    double s = Math.Max(0.0, Math.Min(1.0, (y - 125.0) / 420.0));
                    double s2 = s * s;
                    double sideSign = (x - 384.0) / 180.0;
                    double wave = Math.Sin(t * 2.1 - s * 2.4 + sideSign * 0.9) * (3.2 * s2);
                    double swing = h1 * 18.0 * s + h2 * 24.0 * s2 + wave;
                    double flare = (Math.Abs(h2) * 4.8 + jumpTrail * 0.55) * s2 * sideSign;
                    double lift = (-Math.Abs(h1) * 4.2 + jumpTrail * 0.65) * s2;
                    return new Point(hp.X + swing + flare, hp.Y + lift);
                });
            }

            // 2. Cat tail
            if (_meshes.ContainsKey("01_tail"))
            {
                double tailSwing = _physTail.Value;
                double wagSpeed = 2.3, wagAmp = 7.5, tailLift = 0.0;
                if (_state == "happy" || _state == "dance" || _state == "wave") { wagSpeed = 9.5; wagAmp = 22.0; tailLift = -0.18; }
                else if (_state == "pout") { wagSpeed = 11.0; wagAmp = 17.0; tailLift = -0.05; }
                else if (_state == "stretch") { wagSpeed = 5.5; wagAmp = 9.0; tailLift = -0.32; }
                else if (_state == "working") { wagSpeed = 4.6; wagAmp = 11.5; }
                else if (_state == "sleepy") { wagSpeed = 1.1; wagAmp = 4.0; tailLift = 0.14; }
                else if (_state == "surprised") { wagSpeed = 13.0; wagAmp = 14.0; tailLift = -0.35; }

                _meshes["01_tail"].Deform(delegate(double x, double y)
                {
                    double dx = x - TailRootX, dy = y - TailRootY;
                    double s = Math.Max(0.0, Math.Min(1.0, Math.Sqrt(dx * dx + dy * dy) / 175.0));
                    double s2 = s * s;
                    double wave = Math.Sin(t * wagSpeed - s * 2.6) * wagAmp * s2;
                    double ang = (tailSwing * 0.16 + tailLift) * s + (wave * 0.011);
                    double ca = Math.Cos(ang), sa = Math.Sin(ang);
                    return new Point(TailRootX + (dx * ca - dy * sa) + shiftX * 0.7 + bx * 3.0,
                                     TailRootY + (dx * sa + dy * ca) + breathLift * 0.35 + wave * 0.45 + jumpTrail * 0.3 * s);
                });
            }

            // 3. Independent Left & Right Anatomical Legs (Squat cushion + Airborne bent knees!)
            DeformLeg("02_leg-r", HipRX, HipRY, KneeRX, KneeRY, ParamLegSpreadR, ParamLegKneeR, ParamLegBendR, ParamLegSquat, false, shiftX + bx * 3.0, pureBreathLift);
            DeformLeg("02_leg-l", HipLX, HipLY, KneeLX, KneeLY, ParamLegSpreadL, ParamLegKneeL, ParamLegBendL, ParamLegSquat, true, shiftX + bx * 3.0, pureBreathLift);

            // 4. Skirt (flares gently when jumping & spreading legs!)
            if (_meshes.ContainsKey("06_bottomwear"))
            {
                double sk = _physSkirt.Value;
                double legFlare = Math.Max(0.0, (ParamLegSpreadR + ParamLegSpreadL) * 0.5);
                _meshes["06_bottomwear"].Deform(delegate(double x, double y)
                {
                    double s = Math.Max(0.0, Math.Min(1.0, (y - 332.0) / 115.0));
                    double dx = x - PivotWaistX, dy = y - PivotWaistY;
                    double sway = sk * 17.0 * s * s + Math.Sin(t * 2.4 + dx * 0.03) * 1.8 * s;
                    double flareX = dx * (1.0 + (legFlare * 0.32 + jumpTrail * 0.004) * s * s);
                    double rx = flareX * cosB - dy * sinB;
                    double ry = flareX * sinB + dy * cosB;
                    return new Point(PivotWaistX + rx + shiftX * 0.8 + bx * 4.5 + sway, PivotWaistY + ry + breathLift * 0.48 + jumpTrail * 0.22 * s);
                });
            }

            // 5. Torso
            if (_meshes.ContainsKey("07_topwear"))
            {
                _meshes["07_topwear"].Deform(delegate(double x, double y)
                {
                    double dx = x - PivotWaistX, dy = y - PivotWaistY;
                    double up = Math.Max(0.0, Math.Min(1.0, (PivotWaistY - y) / 155.0));
                    double chestExpand = 1.0 + br * 0.022 * up + st * 0.04 * up;
                    double rx = (dx * chestExpand) * cosB - dy * sinB;
                    double ry = dx * sinB + dy * cosB;
                    return new Point(PivotWaistX + rx + shiftX * (0.8 + 0.2 * up) + bx * (4.5 + 3.0 * up),
                                     PivotWaistY + ry + breathLift * (0.4 + 0.6 * up));
                });
            }

            // 6. Neck + Choker
            if (_meshes.ContainsKey("05_neck"))
            {
                _meshes["05_neck"].Deform(delegate(double x, double y)
                {
                    double up = Math.Max(0.0, Math.Min(1.0, (198.0 - y) / 46.0));
                    Point hp = headTransform(x, y, 0.0);
                    double tx = x + shiftX + bx * 6.0, ty = y + breathLift * 0.9;
                    return new Point(tx + (hp.X - tx) * up, ty + (hp.Y - ty) * up);
                });
            }

            // 7. Sleeves + Independent Swappable Hand Models (Zero wobble when hip-locked gripping waist!)
            double armShift = shiftX + bx * 5.2 + bz * 6.0;
            double armShiftLocked = shiftX + bx * 4.5;
            Point gripR = DeformSleeveAndHand("03_sleeve-r", _handRImg,
                ShoulderRX, ShoulderRY, ElbowRX, ElbowRY, WristRX, WristRY,
                ParamHipLockR ? ParamUpperArmR : (ParamUpperArmR + br * 0.03 - _physArmR.Value * 0.12),
                ParamHipLockR ? ParamElbowR : (ParamElbowR - _physArmR.Value * 0.10),
                ParamHipLockR ? ParamWristR : (ParamWristR - _physArmR.Value * 0.08),
                false, ParamHipLockR, ParamHipLockR ? armShiftLocked : armShift, breathLift, t);

            DeformSleeveAndHand("04_sleeve-l", _handLImg,
                ShoulderLX, ShoulderLY, ElbowLX, ElbowLY, WristLX, WristLY,
                ParamHipLockL ? ParamUpperArmL : (ParamUpperArmL + br * 0.03 + _physArmL.Value * 0.12),
                ParamHipLockL ? ParamElbowL : (ParamElbowL + _physArmL.Value * 0.10),
                ParamHipLockL ? ParamWristL : (ParamWristL + _physArmL.Value * 0.08),
                true, ParamHipLockL, ParamHipLockL ? armShiftLocked : armShift, breathLift, t);

            // 7B. Conjured Gemini Magic Wand anchored directly in gripped Right Hand!
            double wandVis = Math.Max(0.0, Math.Min(1.0, ParamWandShow));
            _wandImg.Opacity = wandVis;
            double wandTipStageX = gripR.X, wandTipStageY = gripR.Y - 115.0;
            if (wandVis > 0.005 || ParamWandFlash > 0.005)
            {
                double wScale = Math.Max(0.01, ParamWandScale);
                _wandScale.ScaleX = wScale;
                _wandScale.ScaleY = wScale;
                _wandRot.Angle = ParamWandAngle;
                // Align local grip pivot (61.0, 192.0) onto exact right-hand grip center!
                _wandShift.X = gripR.X - 61.0;
                _wandShift.Y = gripR.Y - 192.0;

                // Compute exact rotated world position of the Wand Star Tip (127.0px above grip along local -Y)
                double wRad = ParamWandAngle * Math.PI / 180.0;
                double tipLen = 127.0 * wScale;
                wandTipStageX = gripR.X + Math.Sin(wRad) * tipLen;
                wandTipStageY = gripR.Y - Math.Cos(wRad) * tipLen;

                // Convert to outer window coordinates for sparkling fairy-dust particle emission!
                double stageLeft = (BaseWinW - CanvasSize * 0.56) * 0.5;
                double stageTop = BaseWinH - 442.0;
                _lastWandTipX = stageLeft + wandTipStageX * 0.56;
                _lastWandTipY = stageTop + (wandTipStageY - ParamJumpY) * 0.56;
            }

            // 8. Face 2.5D contour
            if (_meshes.ContainsKey("08_face"))
            {
                double puff = ParamMouthGrumpy * 0.048;
                _meshes["08_face"].Deform(delegate(double x, double y)
                {
                    double nx = (x - FaceCenterX) / 52.0, ny = (y - FaceCenterY) / 52.0;
                    double faceDepth = 0.22 * (1.0 - Math.Min(1.0, nx * nx * 0.5 + ny * ny * 0.4));
                    double cheekZone = Math.Max(0.0, 1.0 - Math.Abs(y - 156.0) / 26.0);
                    return headTransform(x + (x - FaceCenterX) * puff * cheekZone, y, faceDepth);
                });
            }

            // 9. Cat Ears (lively listening sway + crisp twitches!)
            double earBase = -ParamEarPerk * 0.20;
            double idleEarWaveR = (_state == "idle") ? (Math.Sin(t * 1.55) * 0.085 + Math.Sin(t * 0.68 + 0.9) * 0.065) : 0.0;
            double idleEarWaveL = (_state == "idle") ? (Math.Sin(t * 1.55 + 0.6) * 0.085 - Math.Sin(t * 0.68) * 0.065) : 0.0;
            DeformEar("20_ears-r", EarRPivotX, EarRPivotY, _physEarR.Value * 0.34 - earBase + idleEarWaveR, headTransform);
            DeformEar("19_ears-l", EarLPivotX, EarLPivotY, _physEarL.Value * 0.34 + earBase + idleEarWaveL, headTransform);

            // 10. Front hair + Expressive Root-Anchored Ahoge (bouncy idle sway + soft-knee drag protection!)
            if (_meshes.ContainsKey("21_front_hair"))
            {
                double fh = 1.25 * Math.Tanh(_physFrontHair.Value / 1.25);
                double ah = 1.45 * Math.Tanh(_physAhoge.Value / 1.45);
                double idleAhogeRad = (_state == "idle")
                    ? (Math.Sin(t * 2.55) * 0.22 + Math.Sin(t * 1.18 + 0.6) * 0.13)
                    : (Math.Sin(t * 3.4) * 0.12);
                double ahogeRad = ah * 0.38 + idleAhogeRad;
                double ahogeBobY = -br * 4.2 - ParamEarPerk * 5.5 - Math.Abs(ahogeRad) * 3.5;

                _meshes["21_front_hair"].Deform(delegate(double x, double y)
                {
                    if (y < 78.0 && x > 348.0 && x < 426.0)
                    {
                        double w = SmoothStep(78.0, 24.0, y);
                        double a = ahogeRad * w;
                        double ca = Math.Cos(a), sa = Math.Sin(a);
                        double dx = x - 384.0, dy = y - 76.0;
                        double rx = 384.0 + dx * ca - dy * sa;
                        double ry = 76.0 + dx * sa + dy * ca + ahogeBobY * w;
                        return headTransform(rx, ry, 1.04);
                    }
                    Point hp = headTransform(x, y, 1.02);
                    double extraX = 0.0;
                    if (y > 92.0)
                    {
                        double s = Math.Min(1.0, (y - 92.0) / 76.0), s2 = s * s;
                        extraX += fh * 11.0 * s2 + Math.Sin(t * 2.5 + (x - 384.0) * 0.04) * 1.8 * s2;
                    }
                    return new Point(hp.X + extraX, hp.Y);
                });
            }

            // 11. Star Hairpin
            if (_meshes.ContainsKey("22_headwear"))
            {
                double fh = _physFrontHair.Value;
                _meshes["22_headwear"].Deform(delegate(double x, double y)
                {
                    Point hp = headTransform(x, y, 1.18);
                    return new Point(hp.X + fh * 2.2, hp.Y);
                });
            }

            // 12. Native Triple-Locked Clipped Facial Rig
            double headDeg = headRad * 180.0 / Math.PI;
            ApplyFacePartTransform(_noseImg, headTransform(FaceCenterX, 149.0, 0.98), FaceCenterX, 149.0, headDeg);
            ApplyFacePartTransform(_mouthImg, headTransform(FaceCenterX, 164.0, 0.78), FaceCenterX, 164.0, headDeg);

            Point mouthPos = headTransform(FaceCenterX, 164.0, 0.78);
            double mdx = mouthPos.X - FaceCenterX, mdy = mouthPos.Y - 164.0;
            if (_mouthOverlayTg == null)
            {
                _mouthOverlayTg = new TransformGroup();
                _mouthOverlayRot = new RotateTransform(0.0, 12, 7);
                _mouthOverlayShift = new TranslateTransform(0.0, 0.0);
                _mouthOverlayTg.Children.Add(_mouthOverlayRot);
                _mouthOverlayTg.Children.Add(_mouthOverlayShift);
                _happyMouthOverlay.RenderTransform = _mouthOverlayTg;
                _surprisedMouthOverlay.RenderTransform = _mouthOverlayTg;
                _poutMouthOverlay.RenderTransform = _mouthOverlayTg;
                _thinkMouthShift = new TranslateTransform(0.0, 0.0);
                _thinkMouthOverlay.RenderTransform = _thinkMouthShift;
                _blushShift = new TranslateTransform(0.0, 0.0);
                _blushR.RenderTransform = _blushShift;
                _blushL.RenderTransform = _blushShift;
            }
            _mouthOverlayRot.Angle = headDeg;
            _mouthOverlayShift.X = mdx; _mouthOverlayShift.Y = mdy;
            _happyMouthOverlay.Opacity = ParamMouthOpen;
            _surprisedMouthOverlay.Opacity = ParamMouthSurprised;
            _poutMouthOverlay.Opacity = ParamMouthGrumpy;
            _thinkMouthShift.X = mdx; _thinkMouthShift.Y = mdy;
            _thinkMouthOverlay.Opacity = ParamMouthPout;

            double mouthMask = Math.Max(Math.Max(ParamMouthOpen, ParamMouthSurprised), Math.Max(ParamMouthGrumpy, ParamMouthPout * 0.75));
            _mouthImg.Opacity = Math.Max(0.0, 1.0 - mouthMask * 0.92);

            Point blPos = headTransform(FaceCenterX, 148.0, 0.55);
            _blushShift.X = blPos.X - FaceCenterX; _blushShift.Y = blPos.Y - 148.0;
            _blushR.Opacity = ParamBlush; _blushL.Opacity = ParamBlush;

            UpdateEyeRig(_eyeRRoot, _eyeRMaskGroup, _eyeROpenScale, _eyeRIrisShift, _eyeRIrisScale,
                _eyelashRImg, _eyelashRShift, _eyelashRScale, _smileLashR, _sleepLashR,
                EyeRCX, EyeRCY, true, ParamEyeROpen, ParamEyeRSmile, ParamEyeSleep, headTransform, headDeg);
            UpdateEyeRig(_eyeLRoot, _eyeLMaskGroup, _eyeLOpenScale, _eyeLIrisShift, _eyeLIrisScale,
                _eyelashLImg, _eyelashLShift, _eyelashLScale, _smileLashL, _sleepLashL,
                EyeLCX, EyeLCY, false, ParamEyeLOpen, ParamEyeLSmile, ParamEyeSleep, headTransform, headDeg);

            double browLift = -ParamBrowY * 4.8;
            Point brPos = headTransform(360, 117, 0.78), blPos2 = headTransform(407, 117, 0.78);
            ApplyFacePartTransform(_browRImg, new Point(brPos.X, brPos.Y + browLift), 360, 117, headDeg + ParamBrowRAngle);
            ApplyFacePartTransform(_browLImg, new Point(blPos2.X, blPos2.Y + browLift), 407, 117, headDeg + ParamBrowLAngle);

            // Wand-Tip Radiant Magic Halo (tracks live wand tip!) & Conjure Summoning Flash (blooms at right hand!)
            _wandTipNode.Opacity = ParamMagicGlow;
            if (ParamMagicGlow > 0.01)
            {
                Canvas.SetLeft(_wandTipNode, wandTipStageX - 72.0);
                Canvas.SetTop(_wandTipNode, wandTipStageY - 72.0);
                _magicOrbRot.Angle = t * 52.0;
                double pulse = 0.90 + 0.14 * Math.Sin(t * 5.2);
                _magicOrbScale.ScaleX = pulse * (0.45 + 0.55 * ParamMagicGlow);
                _magicOrbScale.ScaleY = pulse * (0.45 + 0.55 * ParamMagicGlow);
            }

            _summonFlashNode.Opacity = ParamWandFlash;
            if (ParamWandFlash > 0.01)
            {
                // Center burst midway between hand grip and wand head for a dramatic out-of-thin-air conjure!
                double fx = gripR.X * 0.55 + wandTipStageX * 0.45;
                double fy = gripR.Y * 0.55 + wandTipStageY * 0.45;
                Canvas.SetLeft(_summonFlashNode, fx - 85.0);
                Canvas.SetTop(_summonFlashNode, fy - 85.0);
                _summonFlashRot.Angle = t * 140.0;
                double fs = 0.45 + ParamWandFlash * 0.85;
                _summonFlashScale.ScaleX = fs;
                _summonFlashScale.ScaleY = fs;
            }

            // Stage Airborne Leap + Bounce + Squash-Stretch!
            _stageBounceShift.Y = _physBounce.Value * 12.0 - ParamJumpY;
            _stageSquashScale.ScaleX = 1.0 + _physSquash.Value;
            _stageSquashScale.ScaleY = 1.0 - _physSquash.Value * 0.85;
            PositionBubble();
        }

        private void DeformEar(string key, double pivX, double pivY, double angleRad, Func<double, double, double, Point> headTransform)
        {
            if (!_meshes.ContainsKey(key)) return;
            _meshes[key].Deform(delegate(double x, double y)
            {
                double dx = x - pivX, dy = y - pivY;
                double dist = Math.Min(1.0, Math.Sqrt(dx * dx + dy * dy) / 62.0);
                double a = angleRad * dist;
                double c = Math.Cos(a), s = Math.Sin(a);
                return headTransform(pivX + dx * c - dy * s, pivY + dx * s + dy * c, -0.18);
            });
        }

        private void ApplyFacePartTransform(UIElement elem, Point targetCenter, double restCx, double restCy, double deg)
        {
            RotateTransform rot;
            TranslateTransform shift;
            if (!_faceRotCache.TryGetValue(elem, out rot))
            {
                TransformGroup tg = new TransformGroup();
                rot = new RotateTransform(deg, restCx, restCy);
                shift = new TranslateTransform(targetCenter.X - restCx, targetCenter.Y - restCy);
                tg.Children.Add(rot);
                tg.Children.Add(shift);
                elem.RenderTransform = tg;
                _faceRotCache[elem] = rot;
                _faceShiftCache[elem] = shift;
            }
            else
            {
                shift = _faceShiftCache[elem];
                rot.Angle = deg;
                rot.CenterX = restCx; rot.CenterY = restCy;
                shift.X = targetCenter.X - restCx;
                shift.Y = targetCenter.Y - restCy;
            }
        }

        private void UpdateEyeRig(
            Grid eyeRoot, Grid maskGroup, ScaleTransform openScale, TranslateTransform irisShift, ScaleTransform irisScale,
            Image lashImg, TranslateTransform lashShift, ScaleTransform lashScale,
            System.Windows.Shapes.Path smileLash, System.Windows.Shapes.Path sleepLash,
            double cx, double cy, bool isRightEye, double eyeOpen, double eyeSmile, double eyeSleep,
            Func<double, double, double, Point> headTransform, double headDeg)
        {
            Point p = headTransform(cx, cy, 0.72);
            ApplyFacePartTransform(eyeRoot, p, cx, cy, headDeg);

            double closedOverride = Math.Max(eyeSmile, eyeSleep);
            double effectiveOpen = eyeOpen * (1.0 - closedOverride);
            openScale.ScaleY = Math.Max(0.03, effectiveOpen);
            maskGroup.Opacity = (effectiveOpen < 0.16) ? (effectiveOpen / 0.16) : 1.0;

            double ex = Math.Max(-1.0, Math.Min(1.0, ParamEyeBallX));
            double ey = Math.Max(-1.0, Math.Min(1.0, ParamEyeBallY));
            double maxLeft = isRightEye ? 3.1 : 1.7;
            double maxRight = isRightEye ? 1.7 : 3.1;
            irisShift.X = (ex < 0.0) ? (ex * maxLeft) : (ex * maxRight);
            irisShift.Y = ey * 1.45;

            irisScale.ScaleX = ParamIrisScale * (1.0 - Math.Abs(ex) * 0.06);
            irisScale.ScaleY = ParamIrisScale * (1.0 - Math.Abs(ey) * 0.05);

            double closedness = 1.0 - effectiveOpen;
            lashShift.Y = closedness * 7.2;
            lashScale.ScaleY = 1.0 - closedness * 0.42;
            lashImg.Opacity = Math.Max(0.0, 1.0 - closedOverride);
            smileLash.Opacity = eyeSmile;
            sleepLash.Opacity = eyeSleep * (1.0 - eyeSmile);
        }

        // MASTER 12-ACTION FULL AUDIT RENDERER (4x3 Grid of ALL 12 Live Actions + Updated Gemini Catgirl Anime Menu!)
        public void RenderFull12Audit(string gridOutPath, string menuOutPath)
        {
            Width = BaseWinW; Height = BaseWinH;
            Measure(new Size(BaseWinW, BaseWinH));
            Arrange(new Rect(0, 0, BaseWinW, BaseWinH));
            UpdateLayout();

            int fw = (int)BaseWinW, fh = (int)BaseWinH;
            Drawing.Bitmap sheet = new Drawing.Bitmap(fw * 4, fh * 3);
            using (Drawing.Graphics g = Drawing.Graphics.FromImage(sheet))
            {
                g.Clear(Drawing.Color.FromArgb(255, 28, 26, 44));

                // 1. IDLE (Relaxed straight arms + lively ear/ahoge pose!)
                ResetParams(); _state = "idle";
                ParamBreath = 0.5; ParamEarPerk = 0.25; _physEarR.Value = -0.35; _physAhoge.Value = 0.45;
                ParamUpperArmR = 0.0; ParamElbowR = 0.0; ParamWristR = 0.0; ParamHandR = "relaxed";
                ParamUpperArmL = 0.0; ParamElbowL = 0.0; ParamWristL = 0.0; ParamHandL = "relaxed";
                ShowBubble("\u4e56\u4e56\u966a\u5728\u4e3b\u4eba\u8eab\u8fb9\u55b5~", 5); _bubbleContainer.Opacity = 1.0;
                ApplyRigDeformation(1.2);
                BlitSelf(g, fw * 0, fh * 0, fw, fh, "01. IDLE (STRAIGHT + EARS/AHOGE)");

                // 2. WAVE (Open palm forward + Left hand locked on waist!)
                ResetParams(); _state = "wave";
                ParamAngleZ = -0.34; ParamBodyAngleZ = -0.20; ParamMouthOpen = 0.94; ParamBlush = 0.58; ParamBrowY = 0.42; ParamEarPerk = 0.80;
                ParamUpperArmR = 0.84; ParamElbowR = 1.46; ParamWristR = 0.16; ParamHandR = "palm";
                ParamUpperArmL = 0.46; ParamElbowL = -1.78; ParamWristL = -0.15; ParamHipLockL = true; ParamHandL = "paw";
                ShowBubble("\u4e3b\u4eba\u56de\u6765\u5566\uff01Gemini \u732b\u5a18\u968f\u65f6\u5f85\u547d\u55b5 \u2726", 5); _bubbleContainer.Opacity = 1.0;
                ApplyRigDeformation(1.0);
                BlitSelf(g, fw * 1, fh * 0, fw, fh, "02. WAVE (PALM + WAIST LOCK)");

                // 3. HAPPY JUMP (Bent elbows & bent knees mid-air apex!)
                ResetParams(); _state = "happy";
                ParamJumpY = 66.0; ParamAngleZ = 0.18; ParamBodyAngleZ = 0.14;
                ParamEyeRSmile = 1.0; ParamEyeLSmile = 1.0; ParamMouthOpen = 1.0; ParamBlush = 0.88; ParamEarPerk = 0.98;
                ParamUpperArmR = 1.04; ParamElbowR = 0.84; ParamWristR = 0.10; ParamHandR = "palm";
                ParamUpperArmL = 0.98; ParamElbowL = 0.78; ParamWristL = 0.10; ParamHandL = "palm";
                ParamLegSpreadR = 0.18; ParamLegKneeR = 0.62; ParamLegBendR = 0.95;
                ParamLegSpreadL = 0.16; ParamLegKneeL = 0.56; ParamLegBendL = 0.88;
                ShowBubble("\u597d\u8036\uff01\u80fd\u966a\u7740\u4e3b\u4eba\u597d\u5f00\u5fc3\u55b5 \u2726", 5); _bubbleContainer.Opacity = 1.0;
                ApplyRigDeformation(1.5);
                BlitSelf(g, fw * 2, fh * 0, fw, fh, "03. HAPPY JUMP (BENT LIMBS)");

                // 4. SHY (Hands hidden behind back + heavy blush!)
                ResetParams(); _state = "shy";
                ParamArmsBehind = true;
                ParamAngleX = 0.44; ParamAngleY = 0.42; ParamAngleZ = -0.44; ParamBodyAngleZ = -0.18;
                ParamEyeBallX = 0.78; ParamEyeBallY = 0.62; ParamBlush = 1.0;
                ParamBrowY = 0.42; ParamBrowRAngle = -10.0; ParamBrowLAngle = 10.0; ParamMouthPout = 0.85; ParamEarPerk = -0.82;
                ParamUpperArmR = 0.16; ParamElbowR = -1.25; ParamUpperArmL = 0.16; ParamElbowL = -1.25;
                ParamLegSpreadR = -0.055; ParamLegKneeR = 0.04; ParamLegSpreadL = -0.055; ParamLegKneeL = 0.04;
                ShowBubble("\u4e3b\u3001\u4e3b\u4eba\u522b\u4e00\u76f4\u76ef\u7740\u6211\u770b\u5566\u55b5///", 5); _bubbleContainer.Opacity = 1.0;
                ApplyRigDeformation(2.0);
                BlitSelf(g, fw * 3, fh * 0, fw, fh, "04. SHY (HANDS BEHIND BACK)");

                // 5. PEACE WINK (Peace V + Waist Lock + Leg Kick!)
                ResetParams(); _state = "wink";
                ParamBodyShiftX = -7.0; ParamBodyAngleZ = 0.24; ParamAngleZ = 0.48; ParamAngleX = -0.20;
                ParamEyeRSmile = 1.0; ParamEyeLOpen = 1.0; ParamMouthOpen = 0.88; ParamBlush = 0.72; ParamBrowY = 0.35; ParamEarPerk = 0.68;
                ParamUpperArmR = 0.72; ParamElbowR = 1.40; ParamWristR = -0.15; ParamHandR = "peace";
                ParamUpperArmL = 0.46; ParamElbowL = -1.78; ParamWristL = -0.15; ParamHipLockL = true; ParamHandL = "paw";
                ParamLegSpreadL = 0.14; ParamLegKneeL = 0.30; ParamLegBendL = 0.36;
                ShowBubble("\u7ed9\u4e3b\u4eba\u7684\u4e13\u5c5e Wink~ \u6bd4\u4e2a\u8036 \u2661", 5); _bubbleContainer.Opacity = 1.0;
                ApplyRigDeformation(2.5);
                BlitSelf(g, fw * 0, fh * 1, fw, fh, "05. PEACE WINK (WAIST LOCK)");

                // 6. IDOL DANCE (Full 4-limb rhythm hop!)
                ResetParams(); _state = "dance";
                ParamBodyShiftX = 14.0; ParamJumpY = 26.0; ParamAngleZ = 0.48; ParamAngleX = 0.36; ParamBodyAngleZ = 0.56; ParamBodyAngleX = 0.38;
                ParamMouthOpen = 0.90; ParamBlush = 0.65; ParamBrowY = 0.50; ParamEarPerk = 0.78;
                ParamUpperArmR = 1.00; ParamElbowR = 1.06; ParamWristR = 0.24; ParamHandR = "peace";
                ParamUpperArmL = 0.35; ParamElbowL = -0.96; ParamWristL = -0.24; ParamHandL = "palm";
                ParamLegSpreadR = 0.12; ParamLegKneeR = 0.42; ParamLegBendR = 0.60;
                _physBackHair1.Value = -0.5; _physBackHair2.Value = -0.7; _physSkirt.Value = -0.55;
                ShowBubble("\u5566\u5566\u5566~ \u7ed9\u4e3b\u4eba\u8df3\u652f\u5076\u50cf\u8282\u62cd\u821e\u55b5 \u266a", 5); _bubbleContainer.Opacity = 1.0;
                ApplyRigDeformation(3.0);
                BlitSelf(g, fw * 1, fh * 1, fw, fh, "06. IDOL DANCE (4-LIMB)");

                // 7. STRETCH PEAK (High overhead catgirl Y-stretch + yawn!)
                ResetParams(); _state = "stretch";
                ParamStretch = 1.0; ParamBreath = 1.0; ParamJumpY = 10.0;
                ParamAngleY = -0.46; ParamAngleZ = 0.20; ParamBodyAngleZ = 0.12;
                ParamEyeRSmile = 0.92; ParamEyeLSmile = 0.92; ParamMouthSurprised = 0.85; ParamBlush = 0.55; ParamEarPerk = -0.68;
                ParamUpperArmR = 1.82; ParamElbowR = 0.58; ParamWristR = -0.25; ParamHandR = "palm";
                ParamUpperArmL = 1.82; ParamElbowL = 0.58; ParamWristL = -0.25; ParamHandL = "palm";
                ShowBubble("\u547c\u554a\u2014\u2014\u966a\u4e3b\u4eba\u4e45\u4e86\u4f38\u4e2a\u5927\u61d2\u8170\u55b5~", 5); _bubbleContainer.Opacity = 1.0;
                ApplyRigDeformation(2.0);
                BlitSelf(g, fw * 2, fh * 1, fw, fh, "07. CAT STRETCH (OVERHEAD)");

                // 8. POUT (Both hands locked on waist + puffy grumpy cheeks!)
                ResetParams(); _state = "pout";
                ParamBodyShiftX = 7.0; ParamBodyAngleZ = 0.26; ParamAngleX = -0.32; ParamAngleZ = 0.34;
                ParamEyeBallX = -0.58; ParamBrowY = -0.55; ParamBrowRAngle = 15.0; ParamBrowLAngle = -15.0;
                ParamMouthGrumpy = 1.0; ParamBlush = 0.68; ParamEarPerk = -0.35;
                ParamUpperArmR = 0.46; ParamElbowR = -1.78; ParamWristR = -0.15; ParamHipLockR = true; ParamHandR = "paw";
                ParamUpperArmL = 0.46; ParamElbowL = -1.78; ParamWristL = -0.15; ParamHipLockL = true; ParamHandL = "paw";
                ParamLegSpreadR = 0.03; ParamLegSpreadL = 0.11;
                ShowBubble("\u54fc\uff01\u4e3b\u4eba\u6b3a\u8d1f\u4eba\uff0c\u53c9\u8170\u751f\u6c14\u5566\u55b5\uff01", 5); _bubbleContainer.Opacity = 1.0;
                ApplyRigDeformation(3.5);
                BlitSelf(g, fw * 3, fh * 1, fw, fh, "08. POUT (BOTH WAIST LOCK)");

                // 9. SLEEPY (Peaceful closed u u eyes!)
                ResetParams(); _state = "sleepy";
                ParamAngleY = 0.55; ParamAngleZ = 0.46; ParamBodyAngleZ = 0.22;
                ParamEyeROpen = 0.0; ParamEyeLOpen = 0.0; ParamEyeSleep = 1.0;
                ParamMouthPout = 0.45; ParamEarPerk = -0.88;
                ParamUpperArmR = -0.04; ParamElbowR = -0.22; ParamUpperArmL = -0.04; ParamElbowL = -0.22;
                ShowBubble("zzZ... \u4e3b\u4eba\u5148\u5fd9\uff0c\u6211\u7a0d\u5fae\u772f\u4e00\u4f1a\u513f\u55b5...", 5); _bubbleContainer.Opacity = 1.0;
                ApplyRigDeformation(4.0);
                BlitSelf(g, fw * 0, fh * 2, fw, fh, "09. SLEEPY (PEACEFUL CLOSED)");

                // 10. SURPRISED (Shock jump + tiny pupils + startled bent knees!)
                ResetParams(); _state = "surprised";
                ParamJumpY = 38.0; ParamAngleY = -0.25; ParamIrisScale = 0.76; ParamBrowY = 0.95; ParamMouthSurprised = 1.0; ParamEarPerk = 1.0;
                ParamUpperArmR = 0.72; ParamElbowR = 1.24; ParamWristR = 0.25; ParamHandR = "palm";
                ParamUpperArmL = 0.72; ParamElbowL = 1.24; ParamWristL = 0.25; ParamHandL = "palm";
                ParamLegSpreadR = 0.18; ParamLegKneeR = 0.44; ParamLegBendR = 0.58;
                ParamLegSpreadL = 0.18; ParamLegKneeL = 0.44; ParamLegBendL = 0.58;
                ShowBubble("\u545c\u54c7\uff01\u5413\u6211\u4e00\u8df3\u55b5\uff0c\u4e3b\u4eba\u574f\u5fc3\u773c\uff01", 5); _bubbleContainer.Opacity = 1.0;
                ApplyRigDeformation(4.5);
                BlitSelf(g, fw * 1, fh * 2, fw, fh, "10. SURPRISED (STARTLED JUMP)");

                // 11. THINKING (Head tilt + curled cheek paw + Left hand locked on waist!)
                ResetParams(); _state = "thinking";
                ParamAngleZ = -0.58; ParamAngleX = 0.35; ParamAngleY = -0.35; ParamBodyAngleX = 0.15; ParamBodyAngleZ = -0.18;
                ParamEyeBallX = 0.72; ParamEyeBallY = -0.65; ParamMouthPout = 1.0;
                ParamBrowY = 0.45; ParamBrowRAngle = 6.0; ParamBrowLAngle = -4.0; ParamEarPerk = -0.25;
                ParamUpperArmR = 0.54; ParamElbowR = 1.84; ParamWristR = 0.28; ParamHandR = "paw";
                ParamUpperArmL = 0.46; ParamElbowL = -1.78; ParamWristL = -0.15; ParamHipLockL = true; ParamHandL = "paw";
                ShowBubble("\u8ba9 Gemini \u732b\u5a18\u5e2e\u4e3b\u4eba\u597d\u597d\u60f3\u60f3\u55b5...", 5); _bubbleContainer.Opacity = 1.0;
                ApplyRigDeformation(5.0);
                BlitSelf(g, fw * 2, fh * 2, fw, fh, "11. THINKING (WAIST LOCK)");

                // 12. MAGIC CAST (Conjured Gemini Star Wand + Magic Circle + Left hand locked on waist!)
                ResetParams(); _state = "working";
                ParamBodyShiftX = -4.0; ParamAngleX = -0.08; ParamAngleZ = -0.18; ParamBodyAngleZ = -0.10;
                ParamMouthOpen = 0.50; ParamBlush = 0.50; ParamBrowY = 0.25; ParamEarPerk = 0.72;
                ParamUpperArmR = 0.58; ParamElbowR = 1.44; ParamWristR = 0.14; ParamHandR = "paw";
                ParamUpperArmL = 0.46; ParamElbowL = -1.78; ParamWristL = -0.15; ParamHipLockL = true; ParamHandL = "paw";
                ParamWandShow = 1.0; ParamWandScale = 1.0; ParamWandFlash = 0.0; ParamWandAngle = -14.0; ParamMagicGlow = 1.0;
                ShowBubble("\u661f\u8fb0\u9b54\u6cd5\u68d2\u53ec\u5524\uff01\u8fd9\u5c31\u5e2e\u4e3b\u4eba\u65bd\u6cd5\u55b5 \u2726", 5); _bubbleContainer.Opacity = 1.0;
                ApplyRigDeformation(1.6);
                BlitSelf(g, fw * 3, fh * 2, fw, fh, "12. MAGIC WAND CAST (WORKING)");
            }
            sheet.Save(gridOutPath, Drawing.Imaging.ImageFormat.Png);
            sheet.Dispose();

            RenderAnimeMenuShowcase(menuOutPath);
        }

        private void RenderAnimeMenuShowcase(string outPath)
        {
            ResourceDictionary res = GetAnimeMenuResources();
            int totalW = 920, totalH = 560;
            Grid scene = new Grid();
            scene.Width = totalW; scene.Height = totalH;
            scene.Background = new SolidColorBrush(Color.FromRgb(30, 28, 46));
            TextOptions.SetTextFormattingMode(scene, TextFormattingMode.Display);

            // Left: Character standing with wand cast!
            ResetParams(); _state = "working";
            ParamUpperArmR = 0.56; ParamElbowR = 1.40; ParamWristR = 0.10; ParamHandR = "paw";
            ParamUpperArmL = 0.46; ParamElbowL = -1.78; ParamWristL = -0.15; ParamHipLockL = true; ParamHandL = "paw";
            ParamWandShow = 1.0; ParamWandScale = 1.0; ParamWandAngle = -14.0; ParamMagicGlow = 1.0;
            ParamMouthOpen = 0.5; ParamBlush = 0.5; ParamEarPerk = 0.7;
            ShowBubble("\u4e3b\u4eba\u56de\u6765\u5566\uff01Gemini \u732b\u5a18\u968f\u65f6\u542c\u5019\u5dee\u9063\u55b5 \u2726", 5); _bubbleContainer.Opacity = 1.0;
            ApplyRigDeformation(1.5);

            _outerCanvas.Measure(new Size(BaseWinW, BaseWinH));
            _outerCanvas.Arrange(new Rect(0, 0, BaseWinW, BaseWinH));
            _outerCanvas.UpdateLayout();
            RenderTargetBitmap charRtb = new RenderTargetBitmap((int)BaseWinW, (int)BaseWinH, 96, 96, PixelFormats.Pbgra32);
            charRtb.Render(_outerCanvas);

            Canvas layout = new Canvas();
            layout.Width = totalW; layout.Height = totalH;
            Image charImg = new Image();
            charImg.Source = charRtb; charImg.Width = BaseWinW; charImg.Height = BaseWinH;
            Canvas.SetLeft(charImg, 8); Canvas.SetTop(charImg, 2);
            layout.Children.Add(charImg);

            // Middle: Main Anime Context Menu Card (exact ResourceDictionary brushes & layout!)
            Border mainCard = MakePreviewMenuCard(res, true, new string[]
            {
                "H|\u2726 \u64ad\u653e\u4e13\u5c5e\u52a8\u4f5c (12 \u79cd)|SUB|HL",
                "I|\u2727 \u684c\u5ba0\u5927\u5c0f|SUB|",
                "SEP",
                "C|\u59cb\u7ec8\u7f6e\u9876|ON|",
                "C|\u663e\u793a\u6c14\u6ce1\u5bf9\u8bdd\u6846|ON|",
                "C|\u773c\u7403\u4e0e\u5934\u90e8\u8ffd\u89c6\u9f20\u6807|ON|",
                "C|\u95f2\u7f6e\u81ea\u4e3b\u968f\u673a\u52a8\u4f5c|OFF|",
                "SEP",
                "I|\u273f \u6253\u5f00\u9879\u76ee\u4e0e PSD \u76ee\u5f55||",
                "I|\u2715 \u9000\u51fa\u684c\u5ba0||"
            });
            Canvas.SetLeft(mainCard, 396); Canvas.SetTop(mainCard, 72);
            layout.Children.Add(mainCard);

            // Right: Expanded Submenu Card (Actions!)
            Border subCard = MakePreviewMenuCard(res, false, new string[]
            {
                "I|\u2726 \u5f85\u673a\u8ffd\u89c6 (Idle)||",
                "I|\u2727 \u624b\u638c\u671d\u524d\u6253\u62db\u547c (Wave)||",
                "I|\u2726 \u5f00\u5fc3\u56db\u80a2\u4f38\u5c55\u8e66\u8df3 (Happy)||",
                "I|\u2661 \u624b\u80cc\u8eab\u540e\u5bb3\u7f9e (Shy)||",
                "I|\u2665 \u6bd4\u8036 Wink \u7728\u5355\u773c (Wink)||",
                "I|\u266a \u56db\u80a2\u5076\u50cf\u8282\u62cd\u821e (Dance)||",
                "I|\u273f \u732b\u732b\u4f38\u61d2\u8170 (Stretch)||",
                "I|\u2726 \u63e1\u62f3\u53c9\u8170\u9f13\u816e (Pout)||",
                "I|\u263d \u72af\u56f0\u6253\u778c\u7761 (Sleepy)||",
                "I|\uff01 \u5413\u4e00\u8df3 (Surprised)||",
                "SEP",
                "I|\u2727 \u6b6a\u5934\u601d\u8003 (Thinking)||",
                "I|\u2726 \u53d8\u51fa\u9b54\u6cd5\u68d2\u65bd\u6cd5 (Magic Cast)||HL"
            });
            Canvas.SetLeft(subCard, 634); Canvas.SetTop(subCard, 36);
            layout.Children.Add(subCard);

            scene.Children.Add(layout);
            scene.Measure(new Size(totalW, totalH));
            scene.Arrange(new Rect(0, 0, totalW, totalH));
            scene.UpdateLayout();

            RenderTargetBitmap rtb = new RenderTargetBitmap(totalW, totalH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(scene);
            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (FileStream fs = File.Create(outPath)) enc.Save(fs);
        }

        private Border MakePreviewMenuCard(ResourceDictionary res, bool withHeader, string[] rows)
        {
            Border card = new Border();
            card.CornerRadius = new CornerRadius(withHeader ? 16 : 15);
            card.Background = (Brush)res["MenuCardBg"];
            card.BorderBrush = (Brush)res["MenuCardBorder"];
            card.BorderThickness = new Thickness(2);
            card.Padding = new Thickness(7, 7, 7, 9);
            card.MinWidth = withHeader ? 228 : 262;
            DropShadowEffect ds = new DropShadowEffect();
            ds.Color = Color.FromRgb(90, 66, 166); ds.BlurRadius = 16; ds.ShadowDepth = 3; ds.Opacity = 0.34;
            card.Effect = ds;

            StackPanel sp = new StackPanel();
            if (withHeader)
            {
                Border hdr = new Border();
                hdr.CornerRadius = new CornerRadius(11);
                hdr.Background = (Brush)res["MenuHeaderBg"];
                hdr.Padding = new Thickness(10, 6.5, 10, 6.5);
                hdr.Margin = new Thickness(1, 0, 1, 6);
                Grid hg = new Grid();
                TextBlock s1 = new TextBlock(); s1.Text = "\u2726"; s1.Foreground = new SolidColorBrush(Color.FromRgb(156, 228, 255)); s1.FontSize = 11; s1.HorizontalAlignment = HorizontalAlignment.Left;
                TextBlock title = new TextBlock(); title.Text = "Gemini \u732b\u5a18 \u00b7 \u4e92\u52a8\u83dc\u5355"; title.Foreground = Brushes.White; title.FontFamily = new FontFamily("Microsoft YaHei UI"); title.FontSize = 12.5; title.FontWeight = FontWeights.Bold; title.HorizontalAlignment = HorizontalAlignment.Center;
                TextBlock s2 = new TextBlock(); s2.Text = "\u2727"; s2.Foreground = new SolidColorBrush(Color.FromRgb(255, 178, 228)); s2.FontSize = 11; s2.HorizontalAlignment = HorizontalAlignment.Right;
                hg.Children.Add(s1); hg.Children.Add(title); hg.Children.Add(s2);
                hdr.Child = hg;
                sp.Children.Add(hdr);
            }

            foreach (string raw in rows)
            {
                if (raw == "SEP")
                {
                    Grid sg = new Grid(); sg.Height = 11; sg.Margin = new Thickness(8, 4, 8, 4);
                    sg.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
                    sg.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
                    sg.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
                    Rectangle r1 = new Rectangle(); r1.Height = 1.4; r1.VerticalAlignment = VerticalAlignment.Center; r1.Fill = (Brush)res["MenuSepBrush"];
                    TextBlock st = new TextBlock(); st.Text = "\u2727"; st.FontSize = 9; st.Foreground = new SolidColorBrush(Color.FromRgb(178, 152, 240)); st.Margin = new Thickness(6, 0, 6, 0); st.HorizontalAlignment = HorizontalAlignment.Center; st.VerticalAlignment = VerticalAlignment.Center;
                    Rectangle r2 = new Rectangle(); r2.Height = 1.4; r2.VerticalAlignment = VerticalAlignment.Center; r2.Fill = (Brush)res["MenuSepBrush"];
                    Grid.SetColumn(r1, 0); Grid.SetColumn(st, 1); Grid.SetColumn(r2, 2);
                    sg.Children.Add(r1); sg.Children.Add(st); sg.Children.Add(r2);
                    sp.Children.Add(sg);
                    continue;
                }
                string[] parts = raw.Split('|');
                string kind = parts[0], text = parts[1];
                string flag1 = parts.Length > 2 ? parts[2] : "";
                string flag2 = parts.Length > 3 ? parts[3] : "";
                bool isHl = (flag1 == "HL" || flag2 == "HL");
                bool isSub = (flag1 == "SUB");

                Border bd = new Border();
                bd.CornerRadius = new CornerRadius(10);
                bd.Margin = new Thickness(1, 1.5, 1, 1.5);
                bd.Padding = new Thickness(9, 6, 10, 6);
                bd.BorderThickness = new Thickness(1.2);
                bd.Background = isHl ? (Brush)res["MenuItemHoverBg"] : Brushes.Transparent;
                bd.BorderBrush = isHl ? new SolidColorBrush(Color.FromRgb(178, 152, 246)) : Brushes.Transparent;

                Grid g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });

                if (kind == "C")
                {
                    TextBlock chk = new TextBlock();
                    chk.Width = 18; chk.FontSize = 12.5; chk.Margin = new Thickness(0, 0, 4, 0);
                    chk.Text = (flag1 == "ON") ? "\u2726" : "\u2727";
                    chk.Foreground = new SolidColorBrush((flag1 == "ON") ? Color.FromRgb(110, 84, 220) : Color.FromRgb(182, 162, 230));
                    chk.HorizontalAlignment = HorizontalAlignment.Center; chk.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(chk, 0); g.Children.Add(chk);
                }

                TextBlock lbl = new TextBlock();
                lbl.Text = text;
                lbl.FontFamily = new FontFamily("Microsoft YaHei UI");
                lbl.FontSize = 13; lbl.FontWeight = FontWeights.SemiBold;
                lbl.Foreground = new SolidColorBrush(isHl ? Color.FromRgb(72, 38, 146) : Color.FromRgb(58, 41, 102));
                lbl.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(lbl, 1); g.Children.Add(lbl);

                if (isSub)
                {
                    TextBlock arr = new TextBlock();
                    arr.Text = "\u25b8"; arr.FontSize = 12.5; arr.Foreground = new SolidColorBrush(Color.FromRgb(138, 112, 222));
                    arr.Margin = new Thickness(12, 0, 0, 0); arr.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(arr, 2); g.Children.Add(arr);
                }
                bd.Child = g;
                sp.Children.Add(bd);
            }
            card.Child = sp;
            return card;
        }

        private void ResetParams()
        {
            ParamAngleX = ParamAngleY = ParamAngleZ = 0;
            ParamBodyAngleX = ParamBodyAngleZ = ParamBodyShiftX = ParamJumpY = 0;
            ParamEyeBallX = ParamEyeBallY = 0;
            ParamEyeROpen = ParamEyeLOpen = 1.0;
            ParamEyeRSmile = ParamEyeLSmile = ParamEyeSleep = 0.0;
            ParamIrisScale = 1.0;
            ParamMouthOpen = ParamMouthPout = ParamMouthSurprised = ParamMouthGrumpy = 0;
            ParamBrowY = ParamBrowRAngle = ParamBrowLAngle = 0;
            ParamUpperArmR = ParamUpperArmL = 0;
            ParamElbowR = ParamElbowL = 0.0;
            ParamWristR = ParamWristL = 0;
            ParamArmsBehind = false;
            ParamHipLockR = ParamHipLockL = false;
            ParamHandR = "relaxed"; ParamHandL = "relaxed";
            ParamLegSpreadR = ParamLegKneeR = ParamLegBendR = 0;
            ParamLegSpreadL = ParamLegKneeL = ParamLegBendL = 0;
            ParamLegSquat = 0;
            ParamEarPerk = ParamStretch = ParamBreath = ParamMagicGlow = ParamBlush = 0;
            ParamWandShow = ParamWandScale = ParamWandFlash = 0;
            ParamWandAngle = -18.0;
            _physBackHair1.Value = _physBackHair2.Value = _physFrontHair.Value = _physAhoge.Value = 0;
            _physEarL.Value = _physEarR.Value = _physTail.Value = _physArmL.Value = _physArmR.Value = _physSkirt.Value = 0;
            _physBounce.Value = _physSquash.Value = 0;
        }

        private void BlitSelf(Drawing.Graphics g, int destX, int destY, int w, int h, string label)
        {
            _outerCanvas.Measure(new Size(w, h));
            _outerCanvas.Arrange(new Rect(0, 0, w, h));
            _outerCanvas.UpdateLayout();

            RenderTargetBitmap rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(_outerCanvas);

            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (MemoryStream ms = new MemoryStream())
            {
                enc.Save(ms); ms.Position = 0;
                using (Drawing.Bitmap bmp = new Drawing.Bitmap(ms))
                    g.DrawImage(bmp, destX, destY, w, h);
            }
            using (Drawing.Font f = new Drawing.Font("Consolas", 11, Drawing.FontStyle.Bold))
                g.DrawString(label, f, Drawing.Brushes.LightCyan, destX + 10, destY + h - 24);
        }

        // ============ LIVE UI, ANIME CONTEXT MENU & 13 FULL-BODY ACTIONS ============

        private ResourceDictionary GetAnimeMenuResources()
        {
            if (_animeMenuRes != null) return _animeMenuRes;
            string xaml =
                "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "  <LinearGradientBrush x:Key=\"MenuCardBg\" StartPoint=\"0,0\" EndPoint=\"1,1\">" +
                "    <GradientStop Color=\"#FAFCFAFF\" Offset=\"0.0\"/>" +
                "    <GradientStop Color=\"#F7F1E9FF\" Offset=\"0.55\"/>" +
                "    <GradientStop Color=\"#F7FCEEFF\" Offset=\"1.0\"/>" +
                "  </LinearGradientBrush>" +
                "  <LinearGradientBrush x:Key=\"MenuCardBorder\" StartPoint=\"0,0\" EndPoint=\"1,1\">" +
                "    <GradientStop Color=\"#F59880F6\" Offset=\"0.0\"/>" +
                "    <GradientStop Color=\"#F57AD0FC\" Offset=\"0.52\"/>" +
                "    <GradientStop Color=\"#F5EE9CE0\" Offset=\"1.0\"/>" +
                "  </LinearGradientBrush>" +
                "  <LinearGradientBrush x:Key=\"MenuHeaderBg\" StartPoint=\"0,0\" EndPoint=\"1,0\">" +
                "    <GradientStop Color=\"#FF433378\" Offset=\"0.0\"/>" +
                "    <GradientStop Color=\"#FF614AA8\" Offset=\"0.52\"/>" +
                "    <GradientStop Color=\"#FF7C5CC4\" Offset=\"1.0\"/>" +
                "  </LinearGradientBrush>" +
                "  <LinearGradientBrush x:Key=\"MenuItemHoverBg\" StartPoint=\"0,0\" EndPoint=\"1,0\">" +
                "    <GradientStop Color=\"#FFECE2FF\" Offset=\"0.0\"/>" +
                "    <GradientStop Color=\"#FFE5F3FF\" Offset=\"0.55\"/>" +
                "    <GradientStop Color=\"#FFFFE4F5\" Offset=\"1.0\"/>" +
                "  </LinearGradientBrush>" +
                "  <LinearGradientBrush x:Key=\"MenuSepBrush\" StartPoint=\"0,0\" EndPoint=\"1,0\">" +
                "    <GradientStop Color=\"#00C8B6FA\" Offset=\"0.0\"/>" +
                "    <GradientStop Color=\"#FFC2AEF8\" Offset=\"0.25\"/>" +
                "    <GradientStop Color=\"#FFF2AEE0\" Offset=\"0.5\"/>" +
                "    <GradientStop Color=\"#FF8CD4FC\" Offset=\"0.75\"/>" +
                "    <GradientStop Color=\"#00C8B6FA\" Offset=\"1.0\"/>" +
                "  </LinearGradientBrush>" +

                "  <Style x:Key=\"AnimeContextMenuStyle\" TargetType=\"{x:Type ContextMenu}\">" +
                "    <Setter Property=\"SnapsToDevicePixels\" Value=\"True\"/>" +
                "    <Setter Property=\"OverridesDefaultStyle\" Value=\"True\"/>" +
                "    <Setter Property=\"HasDropShadow\" Value=\"False\"/>" +
                "    <Setter Property=\"Background\" Value=\"Transparent\"/>" +
                "    <Setter Property=\"BorderThickness\" Value=\"0\"/>" +
                "    <Setter Property=\"Template\">" +
                "      <Setter.Value>" +
                "        <ControlTemplate TargetType=\"{x:Type ContextMenu}\">" +
                "          <Grid Margin=\"12\">" +
                "            <Border CornerRadius=\"16\" Background=\"{StaticResource MenuCardBg}\" BorderBrush=\"{StaticResource MenuCardBorder}\" BorderThickness=\"2\" Padding=\"7,7,7,9\" MinWidth=\"214\">" +
                "              <Border.Effect>" +
                "                <DropShadowEffect Color=\"#5A42A6\" BlurRadius=\"16\" ShadowDepth=\"3\" Opacity=\"0.34\"/>" +
                "              </Border.Effect>" +
                "              <StackPanel>" +
                "                <Border CornerRadius=\"11\" Background=\"{StaticResource MenuHeaderBg}\" Padding=\"10,6.5\" Margin=\"1,0,1,6\">" +
                "                  <Grid>" +
                "                    <TextBlock Text=\"\u2726\" Foreground=\"#FF9CE4FF\" FontSize=\"11\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\"/>" +
                "                    <TextBlock Text=\"Gemini \u732b\u5a18 \u00b7 \u4e92\u52a8\u83dc\u5355\" Foreground=\"#FFFDFBFF\" FontFamily=\"Microsoft YaHei UI\" FontSize=\"12.5\" FontWeight=\"Bold\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"/>" +
                "                    <TextBlock Text=\"\u2727\" Foreground=\"#FFFFB2E4\" FontSize=\"11\" HorizontalAlignment=\"Right\" VerticalAlignment=\"Center\"/>" +
                "                  </Grid>" +
                "                </Border>" +
                "                <StackPanel IsItemsHost=\"True\" KeyboardNavigation.DirectionalNavigation=\"Cycle\"/>" +
                "              </StackPanel>" +
                "            </Border>" +
                "          </Grid>" +
                "        </ControlTemplate>" +
                "      </Setter.Value>" +
                "    </Setter>" +
                "  </Style>" +

                "  <Style TargetType=\"{x:Type Separator}\">" +
                "    <Setter Property=\"OverridesDefaultStyle\" Value=\"True\"/>" +
                "    <Setter Property=\"Margin\" Value=\"8,4,8,4\"/>" +
                "    <Setter Property=\"Template\">" +
                "      <Setter.Value>" +
                "        <ControlTemplate TargetType=\"{x:Type Separator}\">" +
                "          <Grid Height=\"11\">" +
                "            <Grid.ColumnDefinitions>" +
                "              <ColumnDefinition Width=\"*\"/>" +
                "              <ColumnDefinition Width=\"Auto\"/>" +
                "              <ColumnDefinition Width=\"*\"/>" +
                "            </Grid.ColumnDefinitions>" +
                "            <Rectangle Grid.Column=\"0\" Height=\"1.4\" VerticalAlignment=\"Center\" Fill=\"{StaticResource MenuSepBrush}\"/>" +
                "            <TextBlock Grid.Column=\"1\" Text=\"\u2727\" FontSize=\"9\" Foreground=\"#FFB298F0\" Margin=\"6,0\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"/>" +
                "            <Rectangle Grid.Column=\"2\" Height=\"1.4\" VerticalAlignment=\"Center\" Fill=\"{StaticResource MenuSepBrush}\"/>" +
                "          </Grid>" +
                "        </ControlTemplate>" +
                "      </Setter.Value>" +
                "    </Setter>" +
                "  </Style>" +

                "  <Style TargetType=\"{x:Type MenuItem}\">" +
                "    <Setter Property=\"OverridesDefaultStyle\" Value=\"True\"/>" +
                "    <Setter Property=\"SnapsToDevicePixels\" Value=\"True\"/>" +
                "    <Setter Property=\"FontFamily\" Value=\"Microsoft YaHei UI, Segoe UI\"/>" +
                "    <Setter Property=\"FontSize\" Value=\"13\"/>" +
                "    <Setter Property=\"FontWeight\" Value=\"SemiBold\"/>" +
                "    <Setter Property=\"Foreground\" Value=\"#FF3A2966\"/>" +
                "    <Setter Property=\"Template\">" +
                "      <Setter.Value>" +
                "        <ControlTemplate TargetType=\"{x:Type MenuItem}\">" +
                "          <Grid>" +
                "            <Border x:Name=\"Bd\" CornerRadius=\"10\" Margin=\"1,1.5\" Padding=\"9,6,10,6\" Background=\"Transparent\" BorderBrush=\"Transparent\" BorderThickness=\"1.2\">" +
                "              <Grid>" +
                "                <Grid.ColumnDefinitions>" +
                "                  <ColumnDefinition Width=\"Auto\"/>" +
                "                  <ColumnDefinition Width=\"*\"/>" +
                "                  <ColumnDefinition Width=\"Auto\"/>" +
                "                </Grid.ColumnDefinitions>" +
                "                <TextBlock x:Name=\"CheckStar\" Grid.Column=\"0\" Width=\"18\" Text=\"\u2727\" FontSize=\"12.5\" Foreground=\"#FFB6A2E6\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Margin=\"0,0,4,0\" Visibility=\"Collapsed\"/>" +
                "                <ContentPresenter Grid.Column=\"1\" ContentSource=\"Header\" VerticalAlignment=\"Center\" RecognizesAccessKey=\"True\"/>" +
                "                <TextBlock x:Name=\"SubArrow\" Grid.Column=\"2\" Text=\"\u25b8\" FontSize=\"12.5\" Foreground=\"#FF8A70DE\" VerticalAlignment=\"Center\" Margin=\"12,0,0,0\" Visibility=\"Collapsed\"/>" +
                "              </Grid>" +
                "            </Border>" +
                "            <Popup x:Name=\"PART_Popup\" AllowsTransparency=\"True\" Placement=\"Right\" HorizontalOffset=\"-6\" VerticalOffset=\"-8\" IsOpen=\"{TemplateBinding IsSubmenuOpen}\" Focusable=\"False\" PopupAnimation=\"Fade\">" +
                "              <Grid Margin=\"12\">" +
                "                <Border CornerRadius=\"15\" Background=\"{StaticResource MenuCardBg}\" BorderBrush=\"{StaticResource MenuCardBorder}\" BorderThickness=\"2\" Padding=\"7,7\" MinWidth=\"220\">" +
                "                  <Border.Effect>" +
                "                    <DropShadowEffect Color=\"#5A42A6\" BlurRadius=\"15\" ShadowDepth=\"3\" Opacity=\"0.32\"/>" +
                "                  </Border.Effect>" +
                "                  <StackPanel IsItemsHost=\"True\" KeyboardNavigation.DirectionalNavigation=\"Cycle\"/>" +
                "                </Border>" +
                "              </Grid>" +
                "            </Popup>" +
                "          </Grid>" +
                "          <ControlTemplate.Triggers>" +
                "            <Trigger Property=\"IsCheckable\" Value=\"True\">" +
                "              <Setter TargetName=\"CheckStar\" Property=\"Visibility\" Value=\"Visible\"/>" +
                "            </Trigger>" +
                "            <Trigger Property=\"IsChecked\" Value=\"True\">" +
                "              <Setter TargetName=\"CheckStar\" Property=\"Text\" Value=\"\u2726\"/>" +
                "              <Setter TargetName=\"CheckStar\" Property=\"Foreground\" Value=\"#FF6E54DC\"/>" +
                "            </Trigger>" +
                "            <Trigger Property=\"Role\" Value=\"SubmenuHeader\">" +
                "              <Setter TargetName=\"SubArrow\" Property=\"Visibility\" Value=\"Visible\"/>" +
                "            </Trigger>" +
                "            <Trigger Property=\"IsHighlighted\" Value=\"True\">" +
                "              <Setter TargetName=\"Bd\" Property=\"Background\" Value=\"{StaticResource MenuItemHoverBg}\"/>" +
                "              <Setter TargetName=\"Bd\" Property=\"BorderBrush\" Value=\"#FFB298F6\"/>" +
                "              <Setter Property=\"Foreground\" Value=\"#FF482692\"/>" +
                "            </Trigger>" +
                "          </ControlTemplate.Triggers>" +
                "        </ControlTemplate>" +
                "      </Setter.Value>" +
                "    </Setter>" +
                "  </Style>" +
                "</ResourceDictionary>";
            _animeMenuRes = (ResourceDictionary)XamlReader.Parse(xaml);
            return _animeMenuRes;
        }

        private void BuildContextMenu()
        {
            ResourceDictionary res = GetAnimeMenuResources();
            ContextMenu menu = new ContextMenu();
            menu.Resources.MergedDictionaries.Add(res);
            menu.Style = (Style)res["AnimeContextMenuStyle"];
            TextOptions.SetTextFormattingMode(menu, TextFormattingMode.Display);

            MenuItem actMenu = new MenuItem();
            actMenu.Header = "\u2726 \u64ad\u653e\u4e13\u5c5e\u52a8\u4f5c (12 \u79cd)";
            actMenu.Items.Add(MakeActionItem("\u2726 \u5f85\u673a\u8ffd\u89c6 (Idle)", delegate { TriggerAction("idle", "\u4e56\u4e56\u966a\u5728\u4e3b\u4eba\u8eab\u8fb9\u55b5~", 0); }));
            actMenu.Items.Add(MakeActionItem("\u2727 \u624b\u638c\u671d\u524d\u6253\u62db\u547c (Wave)", delegate { TriggerAction("wave", "\u4e3b\u4eba\u597d\u5440\uff01\u968f\u65f6\u542c\u5019\u5dee\u9063\u55b5 \u2726", 4.5); }));
            actMenu.Items.Add(MakeActionItem("\u2726 \u5f00\u5fc3\u56db\u80a2\u4f38\u5c55\u8e66\u8df3 (Happy Jump)", delegate { TriggerAction("happy", "\u597d\u8036\uff01\u80fd\u966a\u7740\u4e3b\u4eba\u597d\u5f00\u5fc3\u55b5 \u2726", 5.2); }));
            actMenu.Items.Add(MakeActionItem("\u2661 \u624b\u80cc\u8eab\u540e\u5bb3\u7f9e (Shy Behind Back)", delegate { TriggerAction("shy", "\u4e3b\u3001\u4e3b\u4eba\u522b\u4e00\u76f4\u76ef\u7740\u6211\u770b\u5566\u55b5///", 5.5); }));
            actMenu.Items.Add(MakeActionItem("\u2665 \u6bd4\u8036 Wink \u7728\u5355\u773c (Peace Wink)", delegate { TriggerAction("wink", "\u7ed9\u4e3b\u4eba\u7684\u4e13\u5c5e Wink~ \u6bd4\u4e2a\u8036 \u2661", 4.5); }));
            actMenu.Items.Add(MakeActionItem("\u266a \u56db\u80a2\u5076\u50cf\u8282\u62cd\u821e (Dance)", delegate { TriggerAction("dance", "\u5566\u5566\u5566~ \u7ed9\u4e3b\u4eba\u8df3\u652f\u5076\u50cf\u8282\u62cd\u821e\u55b5 \u266a", 8.5); }));
            actMenu.Items.Add(MakeActionItem("\u273f \u732b\u732b\u4f38\u61d2\u8170 (Stretch)", delegate { TriggerAction("stretch", "\u547c\u554a\u2014\u2014\u966a\u4e3b\u4eba\u4e45\u4e86\u4f38\u4e2a\u5927\u61d2\u8170\u771f\u8212\u670d\u55b5~", 4.7); }));
            actMenu.Items.Add(MakeActionItem("\u2726 \u63e1\u62f3\u53c9\u8170\u9f13\u816e (Pout)", delegate { TriggerAction("pout", "\u54fc\uff01\u4e3b\u4eba\u6b3a\u8d1f\u4eba\uff0c\u53c9\u8170\u751f\u6c14\u5566\u55b5\uff01", 5.5); }));
            actMenu.Items.Add(MakeActionItem("\u263d \u72af\u56f0\u6253\u778c\u7761 (Sleepy)", delegate { TriggerAction("sleepy", "zzZ... \u4e3b\u4eba\u5148\u5fd9\uff0c\u6211\u7a0d\u5fae\u772f\u4e00\u4f1a\u513f\u55b5...", 18.0); }));
            actMenu.Items.Add(MakeActionItem("\uff01 \u5413\u4e00\u8df3 (Surprised)", delegate { TriggerAction("surprised", "\u545c\u54c7\uff01\u5413\u6211\u4e00\u8df3\u55b5\uff0c\u4e3b\u4eba\u574f\u5fc3\u773c\uff01", 3.5); }));
            actMenu.Items.Add(new Separator());
            actMenu.Items.Add(MakeActionItem("\u2727 \u6b6a\u5934\u601d\u8003 (Thinking)", delegate { TriggerAction("thinking", "\u8ba9 Gemini \u732b\u5a18\u5e2e\u4e3b\u4eba\u597d\u597d\u60f3\u60f3\u55b5...", 10.0); }));
            actMenu.Items.Add(MakeActionItem("\u2726 \u53d8\u51fa\u9b54\u6cd5\u68d2\u65bd\u6cd5 (Magic Cast)", delegate { TriggerAction("working", "\u661f\u8fb0\u9b54\u6cd5\u68d2\u53ec\u5524\uff01\u8fd9\u5c31\u5e2e\u4e3b\u4eba\u65bd\u6cd5\u5e72\u6d3b\u55b5 \u2726", 10.0); }));
            menu.Items.Add(actMenu);

            MenuItem sizeMenu = new MenuItem();
            sizeMenu.Header = "\u2727 \u684c\u5ba0\u5927\u5c0f";
            _sizeItemSmall = MakeSizeItem("\u5c0f\u53f7 (75%)", 0.75);
            _sizeItemMedium = MakeSizeItem("\u4e2d\u53f7 (100%)", 1.0);
            _sizeItemLarge = MakeSizeItem("\u5927\u53f7 (125%)", 1.25);
            sizeMenu.Items.Add(_sizeItemSmall);
            sizeMenu.Items.Add(_sizeItemMedium);
            sizeMenu.Items.Add(_sizeItemLarge);
            SyncSizeMenuChecks();
            menu.Items.Add(sizeMenu);

            menu.Items.Add(new Separator());

            MenuItem topItem = new MenuItem();
            topItem.Header = "\u59cb\u7ec8\u7f6e\u9876";
            topItem.IsCheckable = true; topItem.IsChecked = Topmost;
            topItem.Click += delegate { Topmost = topItem.IsChecked; SaveSettings(); };
            menu.Items.Add(topItem);

            MenuItem bubbleItem = new MenuItem();
            bubbleItem.Header = "\u663e\u793a\u6c14\u6ce1\u5bf9\u8bdd\u6846";
            bubbleItem.IsCheckable = true; bubbleItem.IsChecked = _showBubbles;
            bubbleItem.Click += delegate { _showBubbles = bubbleItem.IsChecked; SaveSettings(); };
            menu.Items.Add(bubbleItem);

            MenuItem followItem = new MenuItem();
            followItem.Header = "\u773c\u7403\u4e0e\u5934\u90e8\u8ffd\u89c6\u9f20\u6807";
            followItem.IsCheckable = true; followItem.IsChecked = _mouseFollow;
            followItem.Click += delegate { _mouseFollow = followItem.IsChecked; SaveSettings(); };
            menu.Items.Add(followItem);

            MenuItem autoItem = new MenuItem();
            autoItem.Header = "\u95f2\u7f6e\u81ea\u4e3b\u968f\u673a\u52a8\u4f5c";
            autoItem.IsCheckable = true; autoItem.IsChecked = _autoActions;
            autoItem.Click += delegate { _autoActions = autoItem.IsChecked; SaveSettings(); };
            menu.Items.Add(autoItem);

            menu.Items.Add(new Separator());
            menu.Items.Add(MakeActionItem("\u273f \u6253\u5f00\u9879\u76ee\u4e0e PSD \u76ee\u5f55", delegate { try { Process.Start("explorer.exe", _baseDir); } catch { } }));
            menu.Items.Add(MakeActionItem("\u2715 \u9000\u51fa\u684c\u5ba0", delegate { ExitApp(); }));

            _rigStage.ContextMenu = menu;
        }

        private MenuItem MakeSizeItem(string header, double scale)
        {
            MenuItem mi = new MenuItem();
            mi.Header = header;
            mi.IsCheckable = true;
            mi.Click += delegate
            {
                ApplyScale(scale, true);
                SyncSizeMenuChecks();
                SaveSettings();
            };
            return mi;
        }

        private void SyncSizeMenuChecks()
        {
            if (_sizeItemSmall != null) _sizeItemSmall.IsChecked = Math.Abs(_petScale - 0.75) < 0.05;
            if (_sizeItemMedium != null) _sizeItemMedium.IsChecked = Math.Abs(_petScale - 1.0) < 0.05;
            if (_sizeItemLarge != null) _sizeItemLarge.IsChecked = Math.Abs(_petScale - 1.25) < 0.05;
        }

        private MenuItem MakeActionItem(string header, Action act)
        {
            MenuItem mi = new MenuItem();
            mi.Header = header;
            mi.Click += delegate { act(); };
            return mi;
        }

        private void BuildTrayIcon()
        {
            _trayIcon = new WinForms.NotifyIcon();
            _trayIcon.Text = "Antigravity Gemini Live2D \u732b\u5a18\u684c\u5ba0";
            _trayIcon.Icon = CreateStarIcon();
            _trayIcon.Visible = true;

            WinForms.ContextMenuStrip cms = new WinForms.ContextMenuStrip();
            cms.Items.Add("\u663e\u793a / \u9690\u85cf\u684c\u5ba0", null, delegate
            {
                Dispatcher.BeginInvoke((Action)delegate
                {
                    if (Visibility == Visibility.Visible) Visibility = Visibility.Hidden;
                    else { Visibility = Visibility.Visible; Activate(); }
                });
            });
            cms.Items.Add("\u5f00\u5fc3\u8e66\u8d77\u6765 \u2726", null, delegate
            {
                Dispatcher.BeginInvoke((Action)delegate
                {
                    Visibility = Visibility.Visible;
                    TriggerAction("happy", "\u597d\u8036\uff01\u4e3b\u4eba\u5f00\u5fc3\u6211\u4e5f\u5f00\u5fc3\u55b5 \u2726", 5.0);
                });
            });
            cms.Items.Add("\u590d\u4f4d\u5230\u53f3\u4e0b\u89d2", null, delegate
            {
                Dispatcher.BeginInvoke((Action)delegate { Visibility = Visibility.Visible; ResetToBottomRight(); SaveSettings(); });
            });
            cms.Items.Add("\u9000\u51fa\u684c\u5ba0", null, delegate
            {
                Dispatcher.BeginInvoke((Action)delegate { ExitApp(); });
            });

            _trayIcon.ContextMenuStrip = cms;
            _trayIcon.DoubleClick += delegate
            {
                Dispatcher.BeginInvoke((Action)delegate
                {
                    Visibility = Visibility.Visible; Activate();
                    TriggerAction("wave", "\u4e3b\u4eba\u6211\u5728\u8fd9\u513f\u5462\uff01\u968f\u65f6\u5f85\u547d\u55b5 \u2726", 4.0);
                });
            };
        }

        private Drawing.Icon CreateStarIcon()
        {
            using (Drawing.Bitmap bmp = new Drawing.Bitmap(32, 32))
            {
                using (Drawing.Graphics g = Drawing.Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(Drawing.Color.Transparent);
                    using (Drawing.SolidBrush bg = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 114, 92, 236)))
                        g.FillEllipse(bg, 2, 2, 28, 28);
                    Drawing.PointF[] star = new Drawing.PointF[]
                    {
                        new Drawing.PointF(16, 4), new Drawing.PointF(19.5f, 12.5f),
                        new Drawing.PointF(28, 16), new Drawing.PointF(19.5f, 19.5f),
                        new Drawing.PointF(16, 28), new Drawing.PointF(12.5f, 19.5f),
                        new Drawing.PointF(4, 16), new Drawing.PointF(12.5f, 12.5f)
                    };
                    using (Drawing.SolidBrush sb = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 248, 244, 255)))
                        g.FillPolygon(sb, star);
                }
                _trayHIcon = bmp.GetHicon();
                return Drawing.Icon.FromHandle(_trayHIcon);
            }
        }

        private void ApplyScale(double scale, bool anchorFeet)
        {
            double oldW = Width;
            double oldH = Height;
            _petScale = scale;
            Width = BaseWinW * scale;
            Height = BaseWinH * scale;
            _outerCanvas.LayoutTransform = new ScaleTransform(scale, scale);
            if (anchorFeet && !double.IsNaN(oldW) && !double.IsNaN(oldH) && !double.IsNaN(Left) && !double.IsNaN(Top))
            {
                Left += (oldW - Width) * 0.5;
                Top += (oldH - Height);
            }
        }

        private void ResetToBottomRight()
        {
            Rect wa = SystemParameters.WorkArea;
            double w = double.IsNaN(Width) ? (BaseWinW * _petScale) : Width;
            double h = double.IsNaN(Height) ? (BaseWinH * _petScale) : Height;
            Left = wa.Right - w - 16;
            Top = wa.Bottom - h - 2;
        }

        private void LoadSettings()
        {
            ResetToBottomRight();
            if (!File.Exists(_settingsPath)) return;
            try
            {
                foreach (string line in File.ReadAllLines(_settingsPath))
                {
                    int idx = line.IndexOf('=');
                    if (idx <= 0) continue;
                    string k = line.Substring(0, idx).Trim();
                    string v = line.Substring(idx + 1).Trim();
                    double d; bool b;
                    if (k == "Left" && double.TryParse(v, out d)) Left = d;
                    else if (k == "Top" && double.TryParse(v, out d)) Top = d;
                    else if (k == "Scale" && double.TryParse(v, out d) && d >= 0.5 && d <= 2.0) _petScale = d;
                    else if (k == "Topmost" && bool.TryParse(v, out b)) Topmost = b;
                    else if (k == "ShowBubbles" && bool.TryParse(v, out b)) _showBubbles = b;
                    else if (k == "MouseFollow" && bool.TryParse(v, out b)) _mouseFollow = b;
                    else if (k == "AutoActions" && bool.TryParse(v, out b)) _autoActions = b;
                }
                if (Left < SystemParameters.VirtualScreenLeft - 120 || Left > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 60 ||
                    Top < SystemParameters.VirtualScreenTop - 120 || Top > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 60)
                    ResetToBottomRight();
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                string content = string.Format(
                    "Left={0}\r\nTop={1}\r\nScale={2}\r\nTopmost={3}\r\nShowBubbles={4}\r\nMouseFollow={5}\r\nAutoActions={6}\r\n",
                    Math.Round(Left), Math.Round(Top), _petScale, Topmost, _showBubbles, _mouseFollow, _autoActions);
                File.WriteAllText(_settingsPath, content, Encoding.UTF8);
            }
            catch { }
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            StartUdpListener();
            StartCompanionWatchdog();
            CompositionTarget.Rendering += OnRenderFrame;
            TriggerAction("wave", "\u4e3b\u4eba\u56de\u6765\u5566\uff01Gemini \u732b\u5a18\u968f\u65f6\u542c\u5019\u5dee\u9063\u55b5 \u2726", 4.8);
        }

        private void StartCompanionWatchdog()
        {
            _watchdogTimer = new System.Windows.Threading.DispatcherTimer();
            _watchdogTimer.Interval = TimeSpan.FromMilliseconds(450);
            _watchdogTimer.Tick += delegate
            {
                if (!_companionMode) return;
                List<int> dead = new List<int>();
                foreach (int pid in _hostPids)
                {
                    bool alive = false;
                    try { Process p = Process.GetProcessById(pid); alive = (p != null && !p.HasExited); }
                    catch { alive = false; }
                    if (!alive) dead.Add(pid);
                }
                foreach (int d in dead) _hostPids.Remove(d);
                if (_hostPids.Count == 0)
                {
                    if (_hostsEmptySince == DateTime.MaxValue) _hostsEmptySince = DateTime.UtcNow;
                    else if ((DateTime.UtcNow - _hostsEmptySince).TotalSeconds >= 1.2) ExitApp();
                }
                else _hostsEmptySince = DateTime.MaxValue;
            };
            _watchdogTimer.Start();
        }

        private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _running = false;
            CompositionTarget.Rendering -= OnRenderFrame;
            if (_watchdogTimer != null) _watchdogTimer.Stop();
            SaveSettings();
            try { if (_udpListener != null) _udpListener.Close(); } catch { }
            try { if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); } } catch { }
            try { if (_trayHIcon != IntPtr.Zero) { DestroyIcon(_trayHIcon); _trayHIcon = IntPtr.Zero; } } catch { }
        }

        private void ExitApp()
        {
            _running = false;
            Close();
            Application.Current.Shutdown();
        }

        private void StartUdpListener()
        {
            _udpThread = new Thread(delegate()
            {
                try
                {
                    _udpListener = new UdpClient(new IPEndPoint(IPAddress.Loopback, UdpPort));
                    IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    while (_running)
                    {
                        byte[] data = _udpListener.Receive(ref remote);
                        string text = Encoding.UTF8.GetString(data);
                        string state = "idle", msg = "";
                        int sep = text.IndexOf('|');
                        if (sep >= 0) { state = text.Substring(0, sep).Trim().ToLowerInvariant(); msg = text.Substring(sep + 1).Trim(); }
                        else state = text.Trim().ToLowerInvariant();

                        Dispatcher.BeginInvoke((Action)delegate { HandleExternalState(state, msg); });
                    }
                }
                catch { }
            });
            _udpThread.IsBackground = true;
            _udpThread.Start();
        }

        private void HandleExternalState(string state, string msg)
        {
            if (state == "attach_host")
            {
                int pid;
                if (int.TryParse(msg, out pid) && pid > 0) { _companionMode = true; _hostPids.Add(pid); _hostsEmptySince = DateTime.MaxValue; }
                return;
            }
            if (state == "detach_host")
            {
                int pid;
                if (int.TryParse(msg, out pid))
                {
                    _hostPids.Remove(pid);
                    if (_companionMode && _hostPids.Count == 0 && _hostsEmptySince == DateTime.MaxValue)
                        _hostsEmptySince = DateTime.UtcNow;
                }
                return;
            }
            if (state == "thinking")
            {
                string m = string.IsNullOrEmpty(msg) ? "\u6b63\u5728\u5e2e\u4e3b\u4eba\u8ba4\u771f\u601d\u8003\u4e2d\u55b5..." : msg;
                if (_state == "thinking")
                {
                    _stateExpireAt = DateTime.UtcNow.AddSeconds(45.0);
                    if (_showBubbles) ShowBubble(m, 6.0);
                }
                else TriggerAction("thinking", m, 45.0);
            }
            else if (state == "working")
            {
                string m = string.IsNullOrEmpty(msg) ? "\u6325\u52a8\u9b54\u6cd5\u68d2\u5e2e\u4e3b\u4eba\u65bd\u6cd5\u5e72\u6d3b\u4e2d\u55b5 \u2726" : msg;
                if (_state == "working" && _stateElapsed >= 0.78)
                {
                    // Already conjured! Keep waving magic wand seamlessly without replaying conjure flash!
                    _stateExpireAt = DateTime.UtcNow.AddSeconds(25.0);
                    if (_showBubbles) ShowBubble(m, 6.0);
                    SpawnWandParticles(4);
                }
                else TriggerAction("working", m, 25.0);
            }
            else if (state == "happy") TriggerAction("happy", string.IsNullOrEmpty(msg) ? "\u641e\u5b9a\u5566\u4e3b\u4eba\uff01\u5feb\u6765\u770b\u770b\u7ed3\u679c\u5427 \u2726" : msg, 5.2);
            else TriggerAction(state, msg, state == "idle" ? 0 : 6.0);
        }

        private void TriggerAction(string newState, string bubbleMsg, double durationSec)
        {
            _state = newState;
            _stateElapsed = 0.0;
            _stateExpireAt = (durationSec > 0) ? DateTime.UtcNow.AddSeconds(durationSec) : DateTime.MaxValue;
            if (!string.IsNullOrEmpty(bubbleMsg) && _showBubbles)
                ShowBubble(bubbleMsg, durationSec > 0 ? Math.Min(durationSec, 6.0) : 3.5);

            if (newState == "happy" || newState == "wave")
            {
                _physSquash.Vel += 1.2; _physEarL.Vel -= 8.0; _physEarR.Vel += 8.0;
                SpawnParticles(9, "happy");
            }
            else if (newState == "wink") { _physBounce.Vel -= 4.5; _physEarR.Vel -= 8.0; _physAhoge.Vel += 6.0; SpawnParticles(5, "hearts"); }
            else if (newState == "dance") { _physBounce.Vel -= 6.5; SpawnParticles(6, "music"); }
            else if (newState == "surprised") { _physBounce.Vel -= 14.0; _physSquash.Vel += 1.6; _physAhoge.Vel += 14.0; _physEarL.Vel -= 12.0; _physEarR.Vel += 12.0; SpawnParticles(6, "alert"); }
            else if (newState == "shy") { _physSquash.Vel += 0.7; SpawnParticles(6, "hearts"); }
            else if (newState == "pout") { _physBounce.Vel -= 5.0; _physTail.Vel -= 10.0; SpawnParticles(5, "pout"); }
            else if (newState == "sleepy") { SpawnParticles(2, "sleep"); }
            else if (newState == "working") { _physAhoge.Vel += 5.0; SpawnParticles(5, "stars"); }
            else if (newState == "thinking") { _physEarL.Vel += 4.0; _physEarR.Vel -= 4.0; SpawnParticles(3, "stars"); }
        }

        private void ShowBubble(string text, double seconds)
        {
            _bubbleText.Text = text;
            if (_bubbleContainer != null)
            {
                _bubbleContainer.InvalidateMeasure();
                _bubbleContainer.Measure(new Size(368.0, 220.0));
                _cachedBubbleHeight = Math.Max(54.0, _bubbleContainer.DesiredSize.Height);
            }
            PositionBubble();
            _bubbleExpireAt = DateTime.UtcNow.AddSeconds(seconds);
        }

        private void PositionBubble()
        {
            if (_bubbleHost == null) return;
            // Clean 5px breath gap above ahoge even on 2-line bubbles, lifting softly when jumping!
            double tailTipY = 119.0 - ParamJumpY * 0.40;
            Canvas.SetTop(_bubbleHost, Math.Max(4.0, tailTipY - _cachedBubbleHeight));
        }

        private void SpawnWandParticles(int count)
        {
            string[] symbols = new string[] { "\u2726", "\u2727", "\u22c6", "\u2605" };
            for (int i = 0; i < count; i++)
            {
                TextBlock tb = new TextBlock();
                tb.Text = symbols[_rng.Next(symbols.Length)];
                tb.FontSize = 11 + _rng.Next(8);
                tb.FontWeight = FontWeights.Bold;
                int cPick = _rng.Next(3);
                Color c = (cPick == 0) ? Color.FromRgb(140, 226, 255)
                        : (cPick == 1) ? Color.FromRgb(204, 166, 255)
                        : Color.FromRgb(255, 174, 232);
                tb.Foreground = new SolidColorBrush(c);

                double sx = _lastWandTipX + (_rng.NextDouble() - 0.5) * 34.0;
                double sy = _lastWandTipY + (_rng.NextDouble() - 0.5) * 34.0;
                Canvas.SetLeft(tb, sx); Canvas.SetTop(tb, sy);
                _particleCanvas.Children.Add(tb);

                Particle p = new Particle();
                p.Visual = tb; p.X = sx; p.Y = sy;
                double ang = _rng.NextDouble() * Math.PI * 2.0;
                double spd = 22.0 + _rng.NextDouble() * 44.0;
                p.Vx = Math.Cos(ang) * spd;
                p.Vy = Math.Sin(ang) * spd - 16.0;
                p.Life = 0.0; p.MaxLife = 0.65 + _rng.NextDouble() * 0.45;
                _particles.Add(p);
            }
        }

        private void SpawnParticles(int count, string kind)
        {
            string[] symbols;
            if (kind == "hearts") symbols = new string[] { "\u2661", "\u2665", "\u2727" };
            else if (kind == "music") symbols = new string[] { "\u266a", "\u266b", "\u2726" };
            else if (kind == "sleep") symbols = new string[] { "z", "Z", "zzZ" };
            else if (kind == "alert") symbols = new string[] { "!", "?!", "\u2726" };
            else if (kind == "pout") symbols = new string[] { "\u2726", "#", "!" };
            else if (kind == "think") symbols = new string[] { "?", "\u2727", "..." };
            else if (kind == "happy") symbols = new string[] { "\u2726", "\u2727", "\u2661", "\u2726" };
            else symbols = new string[] { "\u2726", "\u2727", "\u22c6" };

            for (int i = 0; i < count; i++)
            {
                TextBlock tb = new TextBlock();
                tb.Text = symbols[_rng.Next(symbols.Length)];
                tb.FontSize = (kind == "sleep") ? (12 + _rng.Next(7)) : (13 + _rng.Next(8));
                tb.FontWeight = FontWeights.Bold;
                Color c;
                if (kind == "hearts" || tb.Text == "\u2661" || tb.Text == "\u2665") c = Color.FromRgb(255, 130, 198);
                else if (kind == "music") c = (_rng.Next(2) == 0) ? Color.FromRgb(145, 232, 255) : Color.FromRgb(255, 190, 245);
                else if (kind == "sleep") c = Color.FromRgb(192, 180, 255);
                else if (kind == "alert" || kind == "pout") c = Color.FromRgb(255, 165, 130);
                else c = (_rng.Next(2) == 0) ? Color.FromRgb(192, 162, 255) : Color.FromRgb(138, 230, 255);
                tb.Foreground = new SolidColorBrush(c);

                double sx = (kind == "sleep") ? (BaseWinW * 0.62 + (_rng.NextDouble() - 0.5) * 28.0) : (BaseWinW * 0.5 + (_rng.NextDouble() - 0.5) * 165.0);
                double sy = (kind == "sleep") ? (110.0 + (_rng.NextDouble() - 0.5) * 18.0) : (135.0 + (_rng.NextDouble() - 0.5) * 85.0);
                Canvas.SetLeft(tb, sx); Canvas.SetTop(tb, sy);
                _particleCanvas.Children.Add(tb);

                Particle p = new Particle();
                p.Visual = tb; p.X = sx; p.Y = sy;
                p.Vx = (kind == "sleep") ? (10.0 + _rng.NextDouble() * 14.0) : ((_rng.NextDouble() - 0.5) * 52.0);
                p.Vy = (kind == "sleep") ? (-22.0 - _rng.NextDouble() * 16.0) : (-42.0 - _rng.NextDouble() * 50.0);
                p.Life = 0.0; p.MaxLife = (kind == "sleep") ? 1.5 : (0.85 + _rng.NextDouble() * 0.55);
                _particles.Add(p);
            }
        }

        private void OnCharMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true; _movedDuringDrag = false; _shakeEnergy = 0.0;
            POINT pt; GetCursorPos(out pt);
            _dragStartScreen = new Point(pt.X, pt.Y);
            _dragLastScreenX = pt.X; _dragLastScreenY = pt.Y;
            _dragStartWinLeft = Left; _dragStartWinTop = Top;
            ((UIElement)sender).CaptureMouse();
            e.Handled = true;
        }

        private void OnCharMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            POINT pt; GetCursorPos(out pt);
            double dx = pt.X - _dragStartScreen.X, dy = pt.Y - _dragStartScreen.Y;
            double stepX = pt.X - _dragLastScreenX, stepY = pt.Y - _dragLastScreenY;
            _dragLastScreenX = pt.X; _dragLastScreenY = pt.Y;

            if ((stepX * _dragVelX) < -20.0 && Math.Abs(stepX) > 10.0)
            {
                _shakeEnergy += Math.Abs(stepX);
                if (_shakeEnergy > 110.0 && _state != "surprised")
                {
                    _shakeEnergy = 0.0;
                    TriggerAction("surprised", "\u545c\u54c7\uff01\u4e3b\u4eba\u522b\u6643\u5566\uff0c\u8981\u88ab\u6447\u6655\u5566\u55b5\uff01", 3.2);
                }
            }

            // Smooth saturation threshold on drag velocity so ultra-fast mouse flicks feel weighty without blowing up hair!
            _dragVelX = 22.0 * Math.Tanh(stepX / 26.0);
            _dragVelY = 20.0 * Math.Tanh(stepY / 26.0);

            if (Math.Abs(dx) > 4 || Math.Abs(dy) > 4) _movedDuringDrag = true;
            if (_movedDuringDrag)
            {
                Matrix m = PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice;
                Left = _dragStartWinLeft + dx * m.M11;
                Top = _dragStartWinTop + dy * m.M22;
            }
        }

        private void OnCharMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;
            _isDragging = false;
            ((UIElement)sender).ReleaseMouseCapture();
            _dragVelX = 0; _dragVelY = 0; _idleMouseTimer = 0.0;

            if (_movedDuringDrag)
            {
                SaveSettings();
                _physBounce.Vel -= 5.0; _physSquash.Vel += 0.9;
            }
            else
            {
                DateTime now = DateTime.UtcNow;
                Point localPos = e.GetPosition(_rigStage);

                if (_state == "sleepy")
                {
                    TriggerAction("surprised", "\u54ce\u5440\uff01\u4e3b\u4eba\u6211\u6ca1\u5077\u61d2\u7761\u89c9\uff0c\u8fd9\u5c31\u9192\u6765\u55b5\uff01", 2.8);
                    _lastClickAt = now; e.Handled = true; return;
                }

                if ((now - _lastClickAt).TotalMilliseconds < 320.0)
                {
                    _lastClickAt = DateTime.MinValue;
                    _headpatStreak = 0; _pokeStreak = 0;
                    TriggerAction("dance", "\u5566\u5566\u5566~ \u7ed9\u4e3b\u4eba\u8df3\u652f\u4e13\u5c5e\u5076\u50cf\u821e\u55b5 \u266a", 8.0);
                    e.Handled = true; return;
                }
                _lastClickAt = now;

                if (localPos.Y < 195.0)
                {
                    if ((now - _lastHeadpatAt).TotalSeconds < 4.5) _headpatStreak++;
                    else _headpatStreak = 1;
                    _lastHeadpatAt = now;

                    if (_headpatStreak >= 3)
                    {
                        _headpatStreak = 0;
                        TriggerAction("shy", "\u4e3b\u3001\u4e3b\u4eba\u522b\u4e00\u76f4\u6478\u5934\u5566\u55b5/// \u5bb3\u7f9e\u628a\u624b\u80cc\u8d77\u6765\u4e86\uff01", 5.2);
                    }
                    else TriggerAction("happy", _headpatLines[_rng.Next(_headpatLines.Length)], 3.2);
                }
                else
                {
                    if ((now - _lastPokeAt).TotalSeconds < 4.5) _pokeStreak++;
                    else _pokeStreak = 1;
                    _lastPokeAt = now;

                    if (_pokeStreak >= 3)
                    {
                        _pokeStreak = 0;
                        TriggerAction("pout", "\u54fc\uff01\u4e3b\u4eba\u518d\u6233\u6211\u5c31\u53c9\u8170\u4e0d\u5e2e\u4f60\u770b\u4ee3\u7801\u5566\u55b5\uff01", 5.0);
                    }
                    else if (_rng.NextDouble() < 0.42)
                    {
                        TriggerAction("wink", "\u7ed9\u4e3b\u4eba\u7684\u4e13\u5c5e Wink~ \u6bd4\u4e2a\u8036 \u2661", 3.8);
                    }
                    else
                    {
                        ShowBubble(_pokeLines[_rng.Next(_pokeLines.Length)], 3.2);
                        _physBounce.Vel -= 5.5; _physSquash.Vel += 0.85;
                        _physTail.Vel += 7.0; _physEarR.Vel -= 6.5;
                        SpawnParticles(5, "stars");
                    }
                }
            }
            e.Handled = true;
        }

        private static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * Math.Max(0.0, Math.Min(1.0, t));
        }

        private void OnRenderFrame(object sender, EventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            double dt = (now - _lastFrameTime).TotalSeconds;
            if (dt <= 0 || dt > 0.08) dt = 0.016;
            _lastFrameTime = now;
            _simTime += dt;
            _stateElapsed += dt;
            double t = _simTime;

            if (_state != "idle" && now >= _stateExpireAt)
            {
                _state = "idle";
                _stateElapsed = 0.0;
                _stateExpireAt = DateTime.MaxValue;
                _nextAutoActionIn = 20.0 + _rng.NextDouble() * 22.0;
            }

            double targetAX = 0.0, targetAY = 0.0, targetAZ = 0.0;
            double targetBX = 0.0, targetBZ = 0.0, targetShiftX = 0.0, targetJumpY = 0.0;
            double targetEyeX = 0.0, targetEyeY = 0.0;

            POINT pt;
            if (GetCursorPos(out pt))
            {
                if (Math.Abs(pt.X - _lastCursorX) > 3 || Math.Abs(pt.Y - _lastCursorY) > 3)
                {
                    _idleMouseTimer = 0.0; _lastCursorX = pt.X; _lastCursorY = pt.Y;
                }
                else _idleMouseTimer += dt;

                if (_mouseFollow && !_isDragging && _state != "sleepy")
                {
                    PresentationSource src = PresentationSource.FromVisual(this);
                    if (src != null && src.CompositionTarget != null)
                    {
                        Matrix m = src.CompositionTarget.TransformFromDevice;
                        double mx = pt.X * m.M11, my = pt.Y * m.M22;
                        double headScreenX = Left + Width * 0.5, headScreenY = Top + Height * 0.34;
                        double nx = Math.Max(-1.0, Math.Min(1.0, (mx - headScreenX) / 420.0));
                        double ny = Math.Max(-1.0, Math.Min(1.0, (my - headScreenY) / 360.0));
                        targetEyeX = nx; targetEyeY = ny;
                        targetAX = nx * 0.85; targetAY = ny * 0.72; targetAZ = nx * 0.35;
                    }
                }
            }

            if (_state == "idle" && _autoActions && !_isDragging)
            {
                _nextAutoActionIn -= dt;
                if (_nextAutoActionIn <= 0.0)
                {
                    if (_idleMouseTimer > 45.0)
                        TriggerAction("sleepy", "zzZ... \u4e3b\u4eba\u597d\u5b89\u9759\u55b5\uff0c\u5148\u772f\u4e00\u4f1a\u513f...", 24.0);
                    else
                    {
                        int pick = _rng.Next(3);
                        if (pick == 0) TriggerAction("stretch", "\u547c\u554a\u2014\u2014\u4f38\u4e2a\u5927\u61d2\u8170\u7ee7\u7eed\u966a\u4e3b\u4eba\u55b5~", 4.7);
                        else if (pick == 1) TriggerAction("wink", "\u4e3b\u4eba\u770b\u8fd9\u8fb9~ \u9001\u4f60\u4e00\u4e2a Wink \u2661", 3.8);
                        else TriggerAction("wave", "\u4e3b\u4eba\u8f9b\u82e6\u5566\uff01\u8bb0\u5f97\u559d\u53e3\u6c34\u4f11\u606f\u4e00\u4e0b\u55b5 \u2726", 4.0);
                    }
                    _nextAutoActionIn = 26.0 + _rng.NextDouble() * 24.0;
                }
            }

            // Defaults
            double targetRSmile = 0.0, targetLSmile = 0.0, targetEyeSleep = 0.0;
            double targetROpen = 1.0, targetLOpen = 1.0;
            double targetIrisScale = 1.0;
            double targetMouthOpen = 0.0, targetMouthPout = 0.0, targetMouthSurp = 0.0, targetMouthGrumpy = 0.0;
            double targetBrowY = 0.0, targetBrowRA = 0.0, targetBrowLA = 0.0;
            double targetUR = 0.0, targetER = 0.0, targetWR = 0.0;
            double targetUL = 0.0, targetEL = 0.0, targetWL = 0.0;
            double targetLR = 0.0, targetKR = 0.0, targetBR = 0.0;
            double targetLL = 0.0, targetKL = 0.0, targetBL = 0.0;
            double targetSquat = 0.0;
            double targetEarPerk = 0.0, targetStretch = 0.0, targetMagic = 0.0, targetBlush = 0.0;
            double targetWandShow = 0.0, targetWandScale = 0.0, targetWandFlash = 0.0, targetWandAngle = -18.0;
            bool targetArmsBehind = false;
            bool targetHipLockR = false, targetHipLockL = false;
            string targetHandR = "relaxed", targetHandL = "relaxed";
            bool allowAutoBlink = true;

            _ambientParticleCooldown -= dt;

            if (_state == "idle")
            {
                // Relaxed straight hanging arms (NO hip grip, NO elbow bend) + subtle lively ear & ahoge breathing!
                targetAZ += Math.Sin(t * 0.9) * 0.14;
                targetAX += Math.Sin(t * 0.55) * 0.08;
                targetBX = targetAX * 0.5; targetBZ = targetAZ * 0.4;
                targetEarPerk = Math.Sin(t * 0.82) * 0.22 + Math.Sin(t * 1.85) * 0.10;
                targetUR = 0.0; targetER = 0.0; targetWR = 0.0; targetHandR = "relaxed";
                targetUL = 0.0; targetEL = 0.0; targetWL = 0.0; targetHandL = "relaxed";
            }
            else if (_state == "wave")
            {
                // Right hand waving front-facing open palm, Left free hand locked firmly on waist!
                targetAZ = -0.34 + Math.Sin(t * 4.4) * 0.12;
                targetBZ = -0.20;
                targetMouthOpen = 0.94; targetBlush = 0.58; targetBrowY = 0.42; targetEarPerk = 0.80;
                double w = Math.Sin(t * 10.5);
                targetUR = 0.82 + w * 0.06;
                targetER = 1.38 + w * 0.24;
                targetWR = 0.10 + Math.Sin(t * 10.5 - 0.5) * 0.38;
                targetUL = 0.46; targetEL = -1.78; targetWL = -0.15; targetHipLockL = true;
                targetHandR = "palm"; targetHandL = "paw";
            }
            else if (_state == "happy")
            {
                // REAL 4-PHASE ANATOMICAL HUMAN JUMP (Anticipation Squat -> Takeoff -> Bent-Knee Airborne Apex -> Landing Cushion!)
                allowAutoBlink = false;
                double cycle = 0.86;
                double u = (_stateElapsed % cycle) / cycle;
                int hopIdx = (int)Math.Floor(_stateElapsed / cycle);
                double hopSide = (hopIdx % 2 == 0) ? 1.0 : -1.0;

                targetRSmile = 1.0; targetLSmile = 1.0;
                targetMouthOpen = 1.0; targetBrowY = 0.72; targetBlush = 0.88; targetEarPerk = 0.98;
                targetHandR = "palm"; targetHandL = "palm";

                if (u < 0.18)
                {
                    // Phase 1: Grounded Squat Anticipation (feet planted, knees flex, bent elbows gather downward!)
                    double p = Math.Sin((u / 0.18) * Math.PI);
                    targetSquat = p * 0.92;
                    targetJumpY = 0.0;
                    targetAZ = -hopSide * 0.12 * p;
                    targetUR = -0.14 * p; targetER = -0.62 * p - 0.15; targetWR = -0.15 * p;
                    targetUL = -0.14 * p; targetEL = -0.62 * p - 0.15; targetWL = -0.15 * p;
                    targetHandR = "paw"; targetHandL = "paw";
                }
                else if (u < 0.80)
                {
                    // Phase 2 & 3: True Parabolic Gravity Arc + Natural Bent Elbows & Bent Knees + Overlapping Follow-Through!
                    double s = (u - 0.18) / 0.62; // 0..1 airborne
                    double air = 4.0 * s * (1.0 - s); // true physics parabola
                    double tuck = Math.Pow(air, 0.78);
                    // Delayed arm follow-through (arms peak slightly after takeoff & float softly as body starts descending!)
                    double sArm = Math.Max(0.0, Math.Min(1.0, (s - 0.05) / 0.95));
                    double armArc = Math.Sin(sArm * Math.PI);
                    double wristFloat = (s - 0.45) * 0.65; // wrists lag down on rise, float lightly up on fall!

                    targetSquat = 0.0;
                    targetJumpY = air * 66.0;
                    targetAZ = hopSide * 0.22 * air;
                    targetBZ = hopSide * 0.16 * air;

                    // Anatomically natural bent-elbow joyful swing (never stiff straight airplane planks!)
                    double asymArmR = (hopSide > 0) ? 1.06 : 0.94;
                    double asymArmL = (hopSide > 0) ? 0.94 : 1.06;
                    targetUR = (0.38 + armArc * 0.68) * asymArmR;
                    targetER = (0.55 + armArc * 0.36) * asymArmR; // curved ~45-52 deg bent elbows!
                    targetWR = wristFloat;
                    targetUL = (0.38 + armArc * 0.68) * asymArmL;
                    targetEL = (0.55 + armArc * 0.36) * asymArmL;
                    targetWL = wristFloat;

                    // Anatomically accurate backward-outward bent knees in mid-air
                    double asymR = (hopSide > 0) ? 1.05 : 0.88;
                    double asymL = (hopSide > 0) ? 0.88 : 1.05;
                    targetLR = tuck * 0.18 * asymR; targetKR = tuck * 0.62 * asymR; targetBR = tuck * 0.96 * asymR;
                    targetLL = tuck * 0.18 * asymL; targetKL = tuck * 0.62 * asymL; targetBL = tuck * 0.96 * asymL;
                }
                else
                {
                    // Phase 4: Soft Landing Knee-Bend Cushion + Bent Balance Arms!
                    double p = Math.Sin(((u - 0.80) / 0.20) * Math.PI);
                    targetSquat = p * 0.85;
                    targetJumpY = 0.0;
                    targetAZ = hopSide * 0.10 * p;
                    targetUR = 0.34 * p + 0.08; targetER = -0.78 * p; targetWR = 0.12 * p;
                    targetUL = 0.34 * p + 0.08; targetEL = -0.78 * p; targetWL = 0.12 * p;
                }
                if (_ambientParticleCooldown <= 0.0) { SpawnParticles(2, "happy"); _ambientParticleCooldown = 0.45; }
            }
            else if (_state == "shy")
            {
                // HANDS HIDDEN BEHIND HER BACK + SHY PIGEON-TOE STANCE + HEAVY BLUSH!
                targetArmsBehind = true;
                double sway = Math.Sin(t * 2.2);
                targetAX = 0.44; targetAY = 0.42;
                targetAZ = -0.44 + sway * 0.12;
                targetBZ = -0.18 + sway * 0.10;
                targetEyeX = 0.78 + Math.Sin(t * 1.6) * 0.12;
                targetEyeY = 0.62;
                targetBlush = 1.0;
                targetBrowY = 0.42; targetBrowRA = -10.0; targetBrowLA = 10.0;
                targetMouthPout = 0.85; targetEarPerk = -0.82;
                targetUR = 0.16; targetER = -1.25 + sway * 0.05;
                targetUL = 0.16; targetEL = -1.25 - sway * 0.05;
                targetLR = -0.055; targetKR = 0.04;
                targetLL = -0.055; targetKL = 0.04;
                if (_ambientParticleCooldown <= 0.0) { SpawnParticles(1, "hearts"); _ambientParticleCooldown = 0.9; }
            }
            else if (_state == "wink")
            {
                // Peace Sign V-hand beside cheek + OTHER hand planted right on waist + sassy hip pop + cute leg kick!
                allowAutoBlink = false;
                double sway = Math.Sin(t * 2.4);
                targetShiftX = -7.0 + sway * 2.5;
                targetAZ = 0.48 + sway * 0.08;
                targetAX = -0.20; targetBZ = 0.24 + sway * 0.05;
                targetRSmile = 1.0; targetLSmile = 0.0; targetLOpen = 1.0;
                targetMouthOpen = 0.88; targetBlush = 0.72; targetBrowY = 0.35; targetEarPerk = 0.68;
                targetUR = 0.72; targetER = 1.40 + Math.Sin(t * 4.5) * 0.08; targetWR = -0.15 + Math.Sin(t * 4.5) * 0.14;
                // Left hand locked rock-steady gripping left waist!
                targetUL = 0.46; targetEL = -1.78; targetWL = -0.15; targetHipLockL = true;
                targetLL = 0.14; targetKL = 0.30; targetBL = 0.36;
                targetHandR = "peace"; targetHandL = "paw";
            }
            else if (_state == "dance")
            {
                // Full 4-Limb Idol Choreography (Peace + Open Palm + Leg Kicks + Rhythmic Hops!)
                double beat = t * 5.6;
                double s = Math.Sin(beat);
                double c = Math.Cos(beat);
                targetShiftX = s * 15.0;
                targetJumpY = Math.Abs(s) * 28.0;
                targetAZ = s * 0.52; targetAX = s * 0.40;
                targetBZ = s * 0.62; targetBX = s * 0.42;
                targetUR = 0.68 + s * 0.36; targetER = 0.05 + s * 1.08; targetWR = s * 0.28;
                targetUL = 0.68 - s * 0.36; targetEL = 0.05 - s * 1.08; targetWL = -s * 0.28;
                targetLR = Math.Max(0.0, s) * 0.14 - 0.02; targetKR = Math.Max(0.0, s) * 0.45; targetBR = Math.Max(0.0, s) * 0.65;
                targetLL = Math.Max(0.0, -s) * 0.14 - 0.02; targetKL = Math.Max(0.0, -s) * 0.45; targetBL = Math.Max(0.0, -s) * 0.65;
                targetSquat = Math.Max(0.0, 1.0 - Math.Abs(s) * 3.0) * 0.38;
                targetMouthOpen = 0.68 + 0.32 * Math.Abs(c);
                targetBlush = 0.65; targetBrowY = 0.5; targetEarPerk = 0.78;
                targetHandR = (s >= 0) ? "peace" : "palm";
                targetHandL = (s < 0) ? "peace" : "palm";
                if (Math.Abs(c) > 0.84 && Math.Sin(beat * 2.0) > 0) { targetRSmile = 0.9; targetLSmile = 0.9; allowAutoBlink = false; }
                if (_ambientParticleCooldown <= 0.0) { SpawnParticles(2, "music"); _ambientParticleCooldown = 0.6; }
            }
            else if (_state == "thinking")
            {
                targetAZ = -0.58 + Math.Sin(t * 1.3) * 0.12;
                targetAX = 0.35 + Math.Sin(t * 0.9) * 0.14;
                targetAY = -0.35;
                targetBX = targetAX * 0.42; targetBZ = targetAZ * 0.32;
                targetEyeX = 0.72 + Math.Sin(t * 1.1) * 0.16;
                targetEyeY = -0.65;
                targetMouthPout = 1.0;
                targetBrowY = 0.45; targetBrowRA = 6.0; targetBrowLA = -4.0;
                targetUR = 0.54; targetER = 1.84 + Math.Sin(t * 2.2) * 0.08; targetWR = 0.28 + Math.Sin(t * 2.2) * 0.12;
                // Left free hand locked rock-steady gripping left waist!
                targetUL = 0.46; targetEL = -1.78; targetWL = -0.15; targetHipLockL = true;
                targetEarPerk = -0.25;
                targetHandR = "paw"; targetHandL = "paw";
                if (_ambientParticleCooldown <= 0.0) { SpawnParticles(1, "think"); _ambientParticleCooldown = 1.4; }
            }
            else if (_state == "working")
            {
                // 2-STAGE CONJURE GEMINI MAGIC WAND OUT OF THIN AIR -> GRACEFUL MAGIC WAND CASTING!
                // Left free hand locked rock-steady on left waist; Right hand conjures & waves the star wand!
                double el = _stateElapsed;
                targetUL = 0.46; targetEL = -1.78; targetWL = -0.15; targetHipLockL = true;
                targetHandR = "paw"; targetHandL = "paw";
                targetBlush = 0.48; targetEarPerk = 0.68;

                if (el < 0.78)
                {
                    // Stage 1 (0..0.78s): Summoning flash circle blooms at right hand & wand pops into existence!
                    double p = el / 0.78;
                    targetAX = -0.18 * p;
                    targetAZ = -0.26 * p;
                    targetBZ = -0.14 * p;
                    targetUR = 0.50; targetER = 1.38; targetWR = 0.05;
                    targetBrowY = 0.42;
                    targetMouthOpen = 0.65 * Math.Sin(p * Math.PI);
                    targetWandFlash = Math.Sin(p * Math.PI);
                    targetWandShow = SmoothStep(0.08, 0.52, p);
                    // Elastic pop-in scale: 0 -> 1.14 -> 1.0!
                    double pop = SmoothStep(0.05, 0.65, p);
                    double over = Math.Sin(Math.Max(0.0, (p - 0.35) / 0.65) * Math.PI) * 0.16;
                    targetWandScale = pop + over;
                    targetWandAngle = -34.0 + p * 16.0;
                    targetMagic = SmoothStep(0.45, 0.95, p) * 0.85;
                    if (_ambientParticleCooldown <= 0.0) { SpawnWandParticles(3); _ambientParticleCooldown = 0.16; }
                }
                else
                {
                    // Stage 2 (0.78s+): Graceful magical-girl wand wave casting + wand-tip magic circle & stardust!
                    double wTime = el - 0.78;
                    double sw = Math.Sin(wTime * 3.8);
                    double swLag = Math.Sin(wTime * 3.8 - 0.55);
                    targetShiftX = -4.0 + sw * 3.5;
                    targetAX = -0.14 + sw * 0.16;
                    targetAZ = -0.24 + sw * 0.18;
                    targetBZ = -0.14 + sw * 0.10;
                    targetBrowY = 0.25;
                    targetMouthOpen = 0.32 + 0.24 * Math.Max(0.0, Math.Sin(wTime * 4.8));
                    targetUR = 0.52 + sw * 0.14;
                    targetER = 1.34 + swLag * 0.22;
                    targetWR = 0.08 + Math.Sin(wTime * 3.8 - 0.9) * 0.28;
                    targetWandShow = 1.0;
                    targetWandScale = 1.0;
                    targetWandFlash = 0.0;
                    targetWandAngle = -18.0 + swLag * 20.0;
                    targetMagic = 1.0;
                    if (_ambientParticleCooldown <= 0.0) { SpawnWandParticles(2); _ambientParticleCooldown = 0.24; }
                }
            }
            else if (_state == "stretch")
            {
                // 3-STAGE LUXURIOUS CATGIRL STRETCH (4.6s):
                // Stage 1 (0..1.25s): Deep inhale, arms sweep high overhead into a tall Y-stretch + slight tiptoe!
                // Stage 2 (1.25..3.15s): Cozy peak stretch with satisfied micro-shiver, cute yawn, tilted head, curled tail!
                // Stage 3 (3.15..4.6s): Contented exhale, peacefully closed eyes, arms float smoothly back down!
                allowAutoBlink = false;
                double el = _stateElapsed;
                targetHandR = "palm"; targetHandL = "palm";

                if (el < 1.25)
                {
                    double p = SmoothStep(0.0, 1.25, el);
                    targetStretch = p;
                    targetJumpY = p * 9.0; // gentle tiptoe lift!
                    targetAY = -0.44 * p;
                    targetAZ = 0.16 * p;
                    targetUR = p * 1.78; targetER = p * 0.56; targetWR = -0.22 * p;
                    targetUL = p * 1.78; targetEL = p * 0.56; targetWL = -0.22 * p;
                    targetRSmile = p * 0.88; targetLSmile = p * 0.88;
                    targetMouthSurp = p * 0.75;
                    targetBlush = p * 0.48;
                    targetEarPerk = -0.62 * p;
                }
                else if (el < 3.15)
                {
                    double p2 = (el - 1.25) / 1.90; // 0..1 peak hold
                    double shiver = Math.Sin(t * 15.0) * 0.025 * Math.Sin(p2 * Math.PI);
                    double sway = Math.Sin(p2 * Math.PI * 2.0);
                    targetStretch = 1.0 + shiver;
                    targetJumpY = 10.0 + shiver * 35.0;
                    targetAY = -0.46;
                    targetAZ = 0.20 * Math.Cos(p2 * Math.PI);
                    targetBZ = 0.12 * Math.Cos(p2 * Math.PI);
                    targetUR = 1.82 + sway * 0.06 + shiver; targetER = 0.58 + shiver; targetWR = -0.25 + shiver * 1.8;
                    targetUL = 1.82 - sway * 0.06 + shiver; targetEL = 0.58 + shiver; targetWL = -0.25 + shiver * 1.8;
                    targetRSmile = 0.92; targetLSmile = 0.92;
                    targetMouthSurp = 0.85 * Math.Sin(Math.Min(1.0, p2 * 1.15) * Math.PI);
                    targetMouthOpen = (p2 > 0.75) ? ((p2 - 0.75) * 2.4) : 0.0;
                    targetBlush = 0.55;
                    targetEarPerk = -0.68;
                }
                else
                {
                    double p3 = SmoothStep(3.15, 4.55, el); // 0..1 relaxing exhale
                    double rem = 1.0 - p3;
                    targetStretch = rem * 0.7;
                    targetJumpY = rem * 8.0;
                    targetAY = -0.18 * rem;
                    targetAZ = -0.15 * Math.Sin(p3 * Math.PI);
                    // Arms lower smoothly down via soft downward-curved elbows into straight relaxed idle!
                    targetUR = Math.Pow(rem, 1.15) * 1.82;
                    targetER = rem * rem * 0.58 - Math.Sin(p3 * Math.PI) * 0.44;
                    targetWR = rem * (-0.25) + Math.Sin(p3 * Math.PI) * 0.20;
                    targetUL = Math.Pow(rem, 1.15) * 1.82;
                    targetEL = rem * rem * 0.58 - Math.Sin(p3 * Math.PI) * 0.44;
                    targetWL = rem * (-0.25) + Math.Sin(p3 * Math.PI) * 0.20;
                    targetEyeSleep = Math.Sin(p3 * Math.PI) * 0.95;
                    targetMouthOpen = Math.Sin(p3 * Math.PI) * 0.55;
                    targetBlush = rem * 0.45;
                    targetEarPerk = -0.35 * rem;
                    if (p3 > 0.35) { targetHandR = "relaxed"; targetHandL = "relaxed"; }
                }
            }
            else if (_state == "pout")
            {
                // Both hands locked rock-steady gripping waist (zero hand wobble while head/hips pout!)
                double huff = Math.Sin(t * 3.2);
                targetShiftX = 6.5 + huff * 3.0;
                targetAX = -0.32; targetAZ = 0.34 + huff * 0.10; targetBZ = 0.26 + huff * 0.07;
                targetEyeX = -0.58; targetEyeY = -0.1;
                targetBrowY = -0.55; targetBrowRA = 15.0; targetBrowLA = -15.0;
                targetMouthGrumpy = 1.0; targetBlush = 0.68;
                targetUR = 0.46; targetER = -1.78; targetWR = -0.15; targetHipLockR = true;
                targetUL = 0.46; targetEL = -1.78; targetWL = -0.15; targetHipLockL = true;
                targetLR = 0.03; targetLL = 0.11;
                targetEarPerk = -0.35;
                targetHandR = "paw"; targetHandL = "paw";
            }
            else if (_state == "surprised")
            {
                allowAutoBlink = false;
                double shockArc = Math.Max(0.0, Math.Sin(Math.Min(1.0, _stateElapsed / 0.65) * Math.PI));
                targetJumpY = shockArc * 38.0;
                targetAY = -0.25;
                targetIrisScale = 0.76 + Math.Sin(t * 28.0) * 0.04;
                targetBrowY = 0.95; targetMouthSurp = 1.0;
                targetUR = 0.72; targetER = 1.24; targetWR = 0.25;
                targetUL = 0.72; targetEL = 1.24; targetWL = 0.25;
                // Startled cat jump pulls knees naturally upward-outward while airborne!
                targetLR = 0.14 * shockArc + 0.04; targetKR = 0.44 * shockArc; targetBR = 0.58 * shockArc;
                targetLL = 0.14 * shockArc + 0.04; targetKL = 0.44 * shockArc; targetBL = 0.58 * shockArc;
                targetEarPerk = 1.0;
                targetHandR = "palm"; targetHandL = "palm";
            }
            else if (_state == "sleepy")
            {
                // Completely closed peaceful sleeping eyes (u u) + gentle slow head nodding & deep breath!
                allowAutoBlink = false;
                double nod = Math.Max(0.0, Math.Sin(t * 0.85));
                targetAY = 0.32 + nod * 0.46;
                targetAZ = 0.40 + Math.Sin(t * 0.55) * 0.20;
                targetBZ = 0.20 + Math.Sin(t * 0.55) * 0.09;
                targetROpen = 0.0; targetLOpen = 0.0;
                targetEyeSleep = 1.0;
                targetMouthPout = 0.45;
                targetUR = -0.04; targetER = -0.22; targetWR = -0.1;
                targetUL = -0.04; targetEL = -0.22; targetWL = -0.1;
                targetEarPerk = -0.88;
                if (_ambientParticleCooldown <= 0.0) { SpawnParticles(1, "sleep"); _ambientParticleCooldown = 1.35; }
            }

            // Smooth Live2D parameter interpolation
            double prevAX = ParamAngleX, prevAZ = ParamAngleZ, prevShift = ParamBodyShiftX;
            ParamAngleX = Lerp(ParamAngleX, targetAX, dt * 6.8);
            ParamAngleY = Lerp(ParamAngleY, targetAY, dt * 6.8);
            ParamAngleZ = Lerp(ParamAngleZ, targetAZ, dt * 6.5);
            ParamBodyAngleX = Lerp(ParamBodyAngleX, targetBX, dt * 4.8);
            ParamBodyAngleZ = Lerp(ParamBodyAngleZ, targetBZ, dt * 4.8);
            ParamBodyShiftX = Lerp(ParamBodyShiftX, targetShiftX, dt * 7.0);
            ParamJumpY = Lerp(ParamJumpY, targetJumpY, dt * 13.0);

            ParamEyeBallX = Lerp(ParamEyeBallX, targetEyeX, dt * 13.0);
            ParamEyeBallY = Lerp(ParamEyeBallY, targetEyeY, dt * 13.0);
            ParamEyeRSmile = Lerp(ParamEyeRSmile, targetRSmile, dt * 11.0);
            ParamEyeLSmile = Lerp(ParamEyeLSmile, targetLSmile, dt * 11.0);
            ParamEyeSleep = Lerp(ParamEyeSleep, targetEyeSleep, dt * 10.0);
            ParamIrisScale = Lerp(ParamIrisScale, targetIrisScale, dt * 14.0);
            ParamMouthOpen = Lerp(ParamMouthOpen, targetMouthOpen, dt * 11.0);
            ParamMouthPout = Lerp(ParamMouthPout, targetMouthPout, dt * 10.0);
            ParamMouthSurprised = Lerp(ParamMouthSurprised, targetMouthSurp, dt * 14.0);
            ParamMouthGrumpy = Lerp(ParamMouthGrumpy, targetMouthGrumpy, dt * 11.0);
            ParamBrowY = Lerp(ParamBrowY, targetBrowY, dt * 9.0);
            ParamBrowRAngle = Lerp(ParamBrowRAngle, targetBrowRA, dt * 9.0);
            ParamBrowLAngle = Lerp(ParamBrowLAngle, targetBrowLA, dt * 9.0);

            ParamUpperArmR = Lerp(ParamUpperArmR, targetUR, dt * 10.0);
            ParamElbowR = Lerp(ParamElbowR, targetER, dt * 10.0);
            ParamWristR = Lerp(ParamWristR, targetWR, dt * 12.0);
            ParamUpperArmL = Lerp(ParamUpperArmL, targetUL, dt * 10.0);
            ParamElbowL = Lerp(ParamElbowL, targetEL, dt * 10.0);
            ParamWristL = Lerp(ParamWristL, targetWL, dt * 12.0);
            ParamArmsBehind = targetArmsBehind;
            ParamHipLockR = targetHipLockR;
            ParamHipLockL = targetHipLockL;
            ParamHandR = targetHandR;
            ParamHandL = targetHandL;

            ParamLegSpreadR = Lerp(ParamLegSpreadR, targetLR, dt * 13.0);
            ParamLegKneeR = Lerp(ParamLegKneeR, targetKR, dt * 13.0);
            ParamLegBendR = Lerp(ParamLegBendR, targetBR, dt * 13.0);
            ParamLegSpreadL = Lerp(ParamLegSpreadL, targetLL, dt * 13.0);
            ParamLegKneeL = Lerp(ParamLegKneeL, targetKL, dt * 13.0);
            ParamLegBendL = Lerp(ParamLegBendL, targetBL, dt * 13.0);
            ParamLegSquat = Lerp(ParamLegSquat, targetSquat, dt * 15.0);

            ParamEarPerk = Lerp(ParamEarPerk, targetEarPerk, dt * 8.0);
            ParamStretch = Lerp(ParamStretch, targetStretch, dt * 7.0);
            ParamMagicGlow = Lerp(ParamMagicGlow, targetMagic, dt * 7.5);
            ParamWandShow = Lerp(ParamWandShow, targetWandShow, dt * 11.0);
            ParamWandScale = Lerp(ParamWandScale, targetWandScale, dt * 12.0);
            ParamWandFlash = Lerp(ParamWandFlash, targetWandFlash, dt * 14.0);
            ParamWandAngle = Lerp(ParamWandAngle, targetWandAngle, dt * 10.0);
            ParamBlush = Lerp(ParamBlush, targetBlush, dt * 7.5);

            double breathRate = (_state == "sleepy") ? 1.1 : (_state == "working" || _state == "dance" || _state == "happy" ? 3.2 : 1.75);
            ParamBreath = 0.5 + 0.5 * Math.Sin(t * breathRate);

            if (allowAutoBlink)
            {
                double blinkFactor = 1.0;
                if (_blinkPhase < 0.0) { _nextBlinkIn -= dt; if (_nextBlinkIn <= 0.0) _blinkPhase = 0.0; }
                else
                {
                    _blinkPhase += dt / 0.19;
                    if (_blinkPhase >= 1.0)
                    {
                        _blinkPhase = -1.0;
                        if (_pendingDoubleBlink > 0) { _pendingDoubleBlink--; _nextBlinkIn = 0.14; }
                        else { _nextBlinkIn = 2.4 + _rng.NextDouble() * 3.4; if (_rng.NextDouble() < 0.22) _pendingDoubleBlink = 1; }
                    }
                    else
                    {
                        double u = (_blinkPhase < 0.45) ? (_blinkPhase / 0.45) : (1.0 - (_blinkPhase - 0.45) / 0.55);
                        blinkFactor = 1.0 - Math.Sin(u * Math.PI * 0.5);
                    }
                }
                ParamEyeROpen = blinkFactor; ParamEyeLOpen = blinkFactor;
            }
            else
            {
                _blinkPhase = -1.0;
                ParamEyeROpen = Lerp(ParamEyeROpen, targetROpen, dt * 10.0);
                ParamEyeLOpen = Lerp(ParamEyeLOpen, targetLOpen, dt * 10.0);
            }

            if (_state != "sleepy")
            {
                // Adorable cat-ear twitches (left, right, or both — with cute double-flicks in idle!)
                _nextEarTwitchIn -= dt;
                if (_nextEarTwitchIn <= 0.0)
                {
                    int side = (_pendingEarDoubleTwitch > 0) ? _lastTwitchSide : _rng.Next(3);
                    if (side == 0) _physEarR.Vel -= 10.2;
                    else if (side == 1) _physEarL.Vel += 10.2;
                    else { _physEarR.Vel -= 9.2; _physEarL.Vel += 9.2; }

                    if (_pendingEarDoubleTwitch > 0)
                    {
                        _pendingEarDoubleTwitch--;
                        _nextEarTwitchIn = (_state == "idle") ? (1.8 + _rng.NextDouble() * 2.2) : (3.2 + _rng.NextDouble() * 3.8);
                    }
                    else
                    {
                        _lastTwitchSide = side;
                        if (_state == "idle" && _rng.NextDouble() < 0.38)
                        {
                            _pendingEarDoubleTwitch = 1;
                            _nextEarTwitchIn = 0.22;
                        }
                        else
                        {
                            _nextEarTwitchIn = (_state == "idle") ? (1.8 + _rng.NextDouble() * 2.2) : (3.2 + _rng.NextDouble() * 3.8);
                        }
                    }
                }

                // Playful idle ahoge boing / wiggle!
                if (_state == "idle")
                {
                    _nextAhogeWiggleIn -= dt;
                    if (_nextAhogeWiggleIn <= 0.0)
                    {
                        double dir = (_rng.Next(2) == 0) ? 1.0 : -1.0;
                        _physAhoge.Vel += dir * 9.8;
                        _nextAhogeWiggleIn = 2.0 + _rng.NextDouble() * 2.4;
                    }
                }
            }

            double headVelX = Math.Max(-4.5, Math.Min(4.5, (ParamAngleX - prevAX) / dt));
            double headVelZ = Math.Max(-4.5, Math.Min(4.5, (ParamAngleZ - prevAZ) / dt));
            double bodyShiftVel = Math.Max(-60.0, Math.Min(60.0, (ParamBodyShiftX - prevShift) / dt));
            double dragImpulseX = Math.Max(-1.05, Math.Min(1.05, -_dragVelX * 0.048 - bodyShiftVel * 0.012));
            double dragImpulseY = Math.Max(-0.85, Math.Min(0.85, -_dragVelY * 0.032));
            double decay = Math.Pow(0.78, dt * 60.0);
            _dragVelX *= decay; _dragVelY *= decay;

            double hairDrive = Math.Max(-1.25, Math.Min(1.25, -headVelX * 0.14 - headVelZ * 0.20 + dragImpulseX));
            _physBackHair1.Step(-ParamAngleZ * 0.45, hairDrive * 0.72, dt);
            _physBackHair2.Step(_physBackHair1.Value * 1.12, hairDrive * 0.40, dt);
            _physFrontHair.Step(-ParamAngleZ * 0.35, hairDrive * 0.58, dt);
            _physAhoge.Step(-ParamAngleZ * 0.55, Math.Max(-1.4, Math.Min(1.4, hairDrive * 1.15 + dragImpulseY)), dt);
            _physEarL.Step(-ParamAngleZ * 0.25, Math.Max(-0.9, Math.Min(0.9, -headVelZ * 0.16 + dragImpulseX * 0.35)), dt);
            _physEarR.Step(-ParamAngleZ * 0.25, Math.Max(-0.9, Math.Min(0.9, -headVelZ * 0.16 + dragImpulseX * 0.35)), dt);
            _physTail.Step(-ParamBodyAngleZ * 0.5, Math.Max(-1.2, Math.Min(1.2, dragImpulseX * 0.95 - headVelX * 0.08)), dt);
            _physArmL.Step(-ParamBodyAngleZ * 0.3, dragImpulseX * 0.50, dt);
            _physArmR.Step(-ParamBodyAngleZ * 0.3, dragImpulseX * 0.50, dt);
            _physSkirt.Step(-ParamBodyAngleZ * 0.45, dragImpulseX * 0.78, dt);
            _physBounce.Step(0.0, 0.0, dt);
            _physSquash.Step(0.0, 0.0, dt);

            ApplyRigDeformation(t);

            double targetBubble = (_showBubbles && now < _bubbleExpireAt) ? 1.0 : 0.0;
            _bubbleContainer.Opacity = Lerp(_bubbleContainer.Opacity, targetBubble, dt * 8.0);

            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                Particle p = _particles[i];
                p.Life += dt;
                if (p.Life >= p.MaxLife) { _particleCanvas.Children.Remove(p.Visual); _particles.RemoveAt(i); continue; }
                p.X += p.Vx * dt; p.Y += p.Vy * dt;
                if (p.Visual.Text != "z" && p.Visual.Text != "Z" && p.Visual.Text != "zzZ") p.Vy += 20.0 * dt;
                double ratio = p.Life / p.MaxLife;
                p.Visual.Opacity = 1.0 - ratio * ratio;
                Canvas.SetLeft(p.Visual, p.X); Canvas.SetTop(p.Visual, p.Y);
            }
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            if (args != null && args.Length >= 3 && args[0] == "--render-audit-12")
            {
                Application app = new Application();
                Live2DPetWindow testWin = new Live2DPetWindow(true, 0);
                testWin.RenderFull12Audit(args[1], args[2]);
                return;
            }

            int hostPid = 0;
            if (args != null && args.Length >= 2 && args[0] == "--host-pid")
                int.TryParse(args[1], out hostPid);

            bool createdNew;
            using (Mutex mutex = new Mutex(true, "AntigravityGeminiLive2DPet_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    if (hostPid > 0)
                    {
                        try
                        {
                            byte[] d = Encoding.UTF8.GetBytes("attach_host|" + hostPid);
                            using (UdpClient u = new UdpClient()) u.Send(d, d.Length, "127.0.0.1", 47291);
                        }
                        catch { }
                    }
                    return;
                }
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                Live2DPetWindow win = new Live2DPetWindow(false, hostPid);
                app.Run(win);
            }
        }
    }
}

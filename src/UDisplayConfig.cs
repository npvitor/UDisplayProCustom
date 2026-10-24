using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace UDisplayPro
{
    public static class D3DShaderCompiler
    {
        [Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ID3DBlob
        {
            [PreserveSig] IntPtr GetBufferPointer();
            [PreserveSig] IntPtr GetBufferSize();
        }

        [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Ansi)]
        private static extern int D3DCompile(
            [MarshalAs(UnmanagedType.LPStr)] string pSrcData,
            IntPtr SrcDataSize,
            [MarshalAs(UnmanagedType.LPStr)] string pSourceName,
            IntPtr pDefines,
            IntPtr pInclude,
            [MarshalAs(UnmanagedType.LPStr)] string pEntrypoint,
            [MarshalAs(UnmanagedType.LPStr)] string pTarget,
            uint Flags1,
            uint Flags2,
            out ID3DBlob ppCode,
            out ID3DBlob ppErrorMsgs);

        [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.Winapi)]
        private static extern int D3DStripShader(
            IntPtr pShaderBytecode,
            IntPtr BytecodeLength,
            uint uStripFlags,
            out ID3DBlob ppStrippedBlob);

        public static byte[] CompilePS(string hlsl)
        {
            ID3DBlob code = null, err = null, stripped = null;
            try
            {
                int hr = D3DCompile(hlsl, (IntPtr)Encoding.ASCII.GetByteCount(hlsl), null, IntPtr.Zero, IntPtr.Zero, "main", "ps_5_0", (1u << 15), 0, out code, out err);
                if (hr < 0 || code == null)
                    throw new Exception("Erro ao compilar shader: " + (err == null ? hr.ToString("X8") : Marshal.PtrToStringAnsi(err.GetBufferPointer(), (int)err.GetBufferSize())));
                int hr2 = D3DStripShader(code.GetBufferPointer(), code.GetBufferSize(), 0x1Fu, out stripped);
                ID3DBlob finalBlob = (hr2 >= 0 && stripped != null) ? stripped : code;
                byte[] res = new byte[(int)finalBlob.GetBufferSize()];
                Marshal.Copy(finalBlob.GetBufferPointer(), res, 0, res.Length);
                return res;
            }
            finally
            {
                if (stripped != null) Marshal.ReleaseComObject(stripped);
                if (err != null) Marshal.ReleaseComObject(err);
                if (code != null) Marshal.ReleaseComObject(code);
            }
        }
    }

    public class ProConfig
    {
        public int ColorMode = 2;        // 0 = BT.709 Limited (16-235), 1 = BT.709 Full (0-255), 2 = BT.601 Limited
        public int Brightness = 0;       // -30 .. +30 (%)
        public int Contrast = 100;       // 60 .. 140 (%)
        public int Saturation = 100;     // 0 .. 160 (%)
        public int GammaX100 = 100;      // 75 .. 140 (1.00 = 100)
        public int ColorTemp = 0;        // 0 = 6500K, 1 = 5500K, 2 = 4500K, 3 = 7500K
        public int Vibrance = 0;         // 0..40, selective saturation with gamut protection
        public int Sharpness = 0;        // 0..50, bounded luma sharpening; 0 compiles out
        public int EdgeSmoothing = 0;    // 0..50, selective diagonal-edge smoothing
        public int CaptureFps = 30;      // Requested capture rate, not measured output FPS
        public void Load(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("#") || line.StartsWith("[") || !line.Contains("=")) continue;
                    string[] parts = line.Split(new char[] { '=' }, 2);
                    string k = parts[0].Trim();
                    string v = parts[1].Trim();
                    int iv;
                    if (int.TryParse(v, out iv))
                    {
                        if (k == "ColorMode") ColorMode = Math.Max(0, Math.Min(2, iv));
                        else if (k == "Brightness") Brightness = Math.Max(-30, Math.Min(30, iv));
                        else if (k == "Contrast") Contrast = Math.Max(60, Math.Min(140, iv));
                        else if (k == "Saturation") Saturation = Math.Max(0, Math.Min(160, iv));
                        else if (k == "GammaX100") GammaX100 = Math.Max(75, Math.Min(140, iv));
                        else if (k == "ColorTemp") ColorTemp = Math.Max(0, Math.Min(3, iv));
                        else if (k == "Vibrance") Vibrance = Math.Max(0, Math.Min(40, iv));
                        else if (k == "Sharpness") Sharpness = Math.Max(0, Math.Min(50, iv));
                        else if (k == "EdgeSmoothing") EdgeSmoothing = Math.Max(0, Math.Min(50, iv));
                        else if (k == "CaptureFps") CaptureFps = (iv == 45 || iv == 60) ? iv : 30;

                    }
                }
            }
            catch (Exception ex) { throw new IOException("Falha ao ler/gravar configurações: " + path, ex); }
        }

        public void Save(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("[UDisplayPro]");
                sb.AppendLine("FormatVersion=2");
                sb.AppendLine("ColorMode=" + ColorMode);
                sb.AppendLine("Brightness=" + Brightness);
                sb.AppendLine("Contrast=" + Contrast);
                sb.AppendLine("Saturation=" + Saturation);
                sb.AppendLine("GammaX100=" + GammaX100);
                sb.AppendLine("ColorTemp=" + ColorTemp);
                sb.AppendLine("Vibrance=" + Vibrance);
                sb.AppendLine("Sharpness=" + Sharpness);
                sb.AppendLine("EdgeSmoothing=" + EdgeSmoothing);
                sb.AppendLine("CaptureFps=" + CaptureFps);
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) { throw new IOException("Falha ao ler/gravar configurações: " + path, ex); }
        }
    }

    public class SegmentedSelect : UniformGrid
    {
        private Button[] buttons;
        private int selectedIndex = 0;
        public event Action<int> SelectionChanged;

        private static readonly Brush BgActive = new SolidColorBrush(Color.FromRgb(37, 99, 235));
        private static readonly Brush BorderActive = new SolidColorBrush(Color.FromRgb(96, 165, 250));
        private static readonly Brush FgActive = Brushes.White;

        private static readonly Brush BgInactive = new SolidColorBrush(Color.FromRgb(15, 23, 42));
        private static readonly Brush BorderInactive = new SolidColorBrush(Color.FromRgb(51, 65, 85));
        private static readonly Brush FgInactive = new SolidColorBrush(Color.FromRgb(203, 213, 225));

        public SegmentedSelect(string[] options)
        {
            this.Rows = 1;
            this.Columns = options.Length;
            buttons = new Button[options.Length];

            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                Button b = ModernUI.CreateCustomButton(options[i], BgInactive, FgInactive, BorderInactive, 6, new Thickness(i == 0 ? 0 : 6, 0, 0, 0), 34);
                b.FontSize = 12.5;
                b.Click += (s, e) => { SelectedIndex = idx; };
                buttons[i] = b;
                this.Children.Add(b);
            }
            UpdateVisuals();
        }

        public int SelectedIndex
        {
            get { return selectedIndex; }
            set
            {
                int clamped = Math.Max(0, Math.Min(buttons.Length - 1, value));
                selectedIndex = clamped;
                UpdateVisuals();
                if (SelectionChanged != null) SelectionChanged(selectedIndex);
            }
        }

        private void UpdateVisuals()
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                bool active = (i == selectedIndex);
                ModernUI.SetButtonColors(buttons[i],
                    active ? BgActive : BgInactive,
                    active ? FgActive : FgInactive,
                    active ? BorderActive : BorderInactive,
                    active ? FontWeights.SemiBold : FontWeights.Normal);
            }
        }
    }

    public static class ModernUI
    {
        public static readonly Brush BgWindow = new SolidColorBrush(Color.FromRgb(15, 23, 42));     // #0F172A
        public static readonly Brush BgHeader = new SolidColorBrush(Color.FromRgb(9, 14, 26));      // #090E1A
        public static readonly Brush BgCard = new SolidColorBrush(Color.FromRgb(30, 41, 59));       // #1E293B
        public static readonly Brush BorderCard = new SolidColorBrush(Color.FromRgb(51, 65, 85));   // #334155
        public static readonly Brush TextPrimary = new SolidColorBrush(Color.FromRgb(241, 245, 249));
        public static readonly Brush TextMuted = new SolidColorBrush(Color.FromRgb(148, 163, 184));
        public static readonly Brush AccentBlue = new SolidColorBrush(Color.FromRgb(56, 189, 248));
        public static readonly Brush AccentGreen = new SolidColorBrush(Color.FromRgb(74, 222, 128));
        public static readonly Brush PrimaryBtnBg = new SolidColorBrush(Color.FromRgb(37, 99, 235));
        public static readonly Brush PrimaryBtnBorder = new SolidColorBrush(Color.FromRgb(96, 165, 250));
        public static readonly Brush SecondaryBtnBg = new SolidColorBrush(Color.FromRgb(30, 41, 59));
        public static readonly Brush SecondaryBtnBorder = new SolidColorBrush(Color.FromRgb(71, 85, 105));

        public static Border CreateCard(string title, string subtitle, UIElement content)
        {
            StackPanel sp = new StackPanel();
            TextBlock tbTitle = new TextBlock
            {
                Text = title,
                Foreground = AccentBlue,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, string.IsNullOrEmpty(subtitle) ? 9 : 2)
            };
            sp.Children.Add(tbTitle);

            if (!string.IsNullOrEmpty(subtitle))
            {
                TextBlock tbSub = new TextBlock
                {
                    Text = subtitle,
                    Foreground = TextMuted,
                    FontSize = 11.8,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 9)
                };
                sp.Children.Add(tbSub);
            }

            sp.Children.Add(content);

            return new Border
            {
                Background = BgCard,
                BorderBrush = BorderCard,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 11, 16, 12),
                Margin = new Thickness(0, 0, 0, 9),
                Child = sp
            };
        }

        public static Button CreateCustomButton(string text, Brush bg, Brush fg, Brush border, double radius, Thickness margin, double height)
        {
            Button btn = new Button
            {
                Content = text,
                Background = bg,
                Foreground = fg,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                Margin = margin,
                Height = height,
                Cursor = Cursors.Hand,
                FontSize = 12.5,
                Padding = new Thickness(6, 0, 6, 0)
            };

            ControlTemplate tpl = new ControlTemplate(typeof(Button));
            FrameworkElementFactory bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "PART_Border";
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            bd.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            bd.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

            FrameworkElementFactory cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);

            tpl.VisualTree = bd;
            btn.Template = tpl;
            return btn;
        }

        public static void SetButtonColors(Button btn, Brush bg, Brush fg, Brush border, FontWeight weight)
        {
            btn.Background = bg;
            btn.Foreground = fg;
            btn.BorderBrush = border;
            btn.FontWeight = weight;
        }

        public static Grid CreateLabeledRow(string labelText, UIElement rightControl, bool isLast)
        {
            Grid g = new Grid { Margin = new Thickness(0, 0, 0, isLast ? 0 : 9) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock lbl = new TextBlock
            {
                Text = labelText,
                Foreground = TextPrimary,
                FontSize = 12.8,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(rightControl, 1);

            g.Children.Add(lbl);
            g.Children.Add(rightControl);
            return g;
        }
    }

    public class MainWindow : Window
    {
        private ProConfig cfg = new ProConfig();
        private string progDataDir = EngineUpdate.InstallDir;
        private bool applying;
        private string appDir;

        private Button[] navTabs = new Button[3];
        private UIElement[] pages = new UIElement[3];

        private SegmentedSelect segColorMode, segColorTemp;
        private Slider sldBrightness, sldContrast, sldSaturation, sldGamma;
        private Slider sldVibrance, sldSharpness, sldEdgeSmoothing;
        private SegmentedSelect segCaptureFps;
        private TextBlock lblBrightnessVal, lblContrastVal, lblSaturationVal, lblGammaVal;


        private TextBlock lblHeaderBadge;
        private TextBlock lblDiagAdapter, lblDiagProcs, lblDiagPatch;
        private TextBlock lblFooter;
        private DispatcherTimer statusTimer;

        public MainWindow()
        {
            appDir = AppDomain.CurrentDomain.BaseDirectory;
            string iniPath = Path.Combine(progDataDir, "UDisplayPro.ini");
            if (!File.Exists(iniPath))
            {
                string localIni = Path.Combine(appDir, "UDisplayPro.ini");
                if (File.Exists(localIni)) iniPath = localIni;
            }
            try
            {
                if (File.Exists(iniPath) && File.ReadAllText(iniPath).Contains("FormatVersion=2")) cfg.Load(iniPath);
            }
            catch (Exception ex) { cfg = new ProConfig(); MessageBox.Show(ex.Message, "Configuração inválida"); }
            BuildWindow();
            LoadConfigToUI();
            UpdateLiveStatus();

            statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            statusTimer.Tick += (s, e) => { if (WindowState != WindowState.Minimized && !applying) UpdateLiveStatus(); };
            statusTimer.Start();
            this.Closing += (sender, e) => { if (applying) e.Cancel = true; };
            this.Closed += (sender, e) => statusTimer.Stop();
            this.Loaded += (sender, e) => {
                if (File.Exists(Path.Combine(progDataDir,"UDisplayPro.pending"))) RunWorker("--recover", null);
            };
        }

        private void BuildWindow()
        {
            this.Title = "UDisplay Pro — Central de Configuração e Calibração (Lemorele LC06 Plus)";
            this.Width = 880;
            this.Height = 795;
            this.MinWidth = 820;
            this.MinHeight = 660;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.Background = ModernUI.BgWindow;
            this.Foreground = ModernUI.TextPrimary;
            this.FontFamily = new FontFamily("Segoe UI");

            try
            {
                string exeIconSrc = Path.Combine(progDataDir, "UDisplay.exe");
                if (!File.Exists(exeIconSrc)) exeIconSrc = Path.Combine(appDir, "UDisplay.exe");
                if (File.Exists(exeIconSrc))
                {
                    using (var ico = System.Drawing.Icon.ExtractAssociatedIcon(exeIconSrc))
                    {
                        if (ico != null)
                        {
                            this.Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        }
                    }
                }
            }
            catch { }

            Grid root = new Grid { Background = ModernUI.BgWindow };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Nav Bar
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

            // 1. Header
            Border headerBorder = new Border
            {
                Background = ModernUI.BgHeader,
                BorderBrush = ModernUI.BorderCard,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(22, 14, 22, 14)
            };
            Grid headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel headerLeft = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            TextBlock tbMainTitle = new TextBlock
            {
                Text = "UDisplay Pro — Calibração de Imagem e Recuperação",
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = ModernUI.AccentBlue
            };
            TextBlock tbSubTitle = new TextBlock
            {
                Text = "Ajustes de cor com backup e reversão automática • Motor original preservado",
                FontSize = 12,
                Foreground = ModernUI.TextMuted,
                Margin = new Thickness(0, 3, 0, 0)
            };
            headerLeft.Children.Add(tbMainTitle);
            headerLeft.Children.Add(tbSubTitle);

            Border statusBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(20, 45, 38)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(34, 120, 80)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, 6, 14, 6),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            lblHeaderBadge = new TextBlock
            {
                Text = "Verificando processos...",
                Foreground = ModernUI.AccentGreen,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold
            };
            statusBadge.Child = lblHeaderBadge;

            Grid.SetColumn(headerLeft, 0);
            Grid.SetColumn(statusBadge, 1);
            headerGrid.Children.Add(headerLeft);
            headerGrid.Children.Add(statusBadge);
            headerBorder.Child = headerGrid;
            Grid.SetRow(headerBorder, 0);
            root.Children.Add(headerBorder);

            // 2. Navigation Bar
            Border navBorder = new Border
            {
                Background = ModernUI.BgWindow,
                Padding = new Thickness(22, 12, 22, 4)
            };
            UniformGrid navGrid = new UniformGrid { Rows = 1, Columns = 3 };
            string[] tabNames = new string[] {
                "1. Calibração de Cor e Contraste",
                "2. Captura e Desempenho",
                "3. Monitores, Diagnóstico e Sistema"
            };
            for (int i = 0; i < 3; i++)
            {
                int tabIdx = i;
                Button b = ModernUI.CreateCustomButton(
                    tabNames[i],
                    ModernUI.SecondaryBtnBg,
                    ModernUI.TextPrimary,
                    ModernUI.SecondaryBtnBorder,
                    8,
                    new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                    38);
                b.Click += (s, e) => SelectTab(tabIdx);
                navTabs[i] = b;
                navGrid.Children.Add(b);
            }
            navBorder.Child = navGrid;
            Grid.SetRow(navBorder, 1);
            root.Children.Add(navBorder);

            // 3. Content Area
            ScrollViewer scroll = new ScrollViewer
            {
                Background = ModernUI.BgWindow,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(22, 8, 22, 8)
            };
            Grid pagesHost = new Grid();
            pages[0] = BuildColorPage();
            pages[1] = BuildPerfPage();
            pages[2] = BuildSystemPage();
            foreach (UIElement p in pages) pagesHost.Children.Add(p);
            scroll.Content = pagesHost;
            Grid.SetRow(scroll, 2);
            root.Children.Add(scroll);

            // 4. Footer Bar
            Border footerBorder = new Border
            {
                Background = ModernUI.BgHeader,
                BorderBrush = ModernUI.BorderCard,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(22, 10, 22, 10)
            };
            Grid footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            lblFooter = new TextBlock
            {
                Text = "Pronto. Ajuste as opções desejadas e clique em 'Aplicar e Atualizar Monitor'.",
                Foreground = ModernUI.AccentGreen,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 16, 0)
            };

            StackPanel footerBtns = new StackPanel { Orientation = Orientation.Horizontal };
            Button btnDefault = ModernUI.CreateCustomButton(
                "Perfil Natural",
                ModernUI.SecondaryBtnBg,
                ModernUI.TextPrimary,
                ModernUI.SecondaryBtnBorder,
                7,
                new Thickness(0, 0, 10, 0),
                36);
            btnDefault.Padding = new Thickness(14, 0, 14, 0);
            btnDefault.Click += (s, e) => ApplyPreset(0);

            Button btnApply = ModernUI.CreateCustomButton(
                "Aplicar e Atualizar Monitor",
                ModernUI.PrimaryBtnBg,
                Brushes.White,
                ModernUI.PrimaryBtnBorder,
                7,
                new Thickness(0),
                36);
            btnApply.FontWeight = FontWeights.SemiBold;
            btnApply.Padding = new Thickness(18, 0, 18, 0);
            btnApply.Click += (s, e) => SaveAndApplyAll();

            footerBtns.Children.Add(btnDefault);
            footerBtns.Children.Add(btnApply);

            Grid.SetColumn(lblFooter, 0);
            Grid.SetColumn(footerBtns, 1);
            footerGrid.Children.Add(lblFooter);
            footerGrid.Children.Add(footerBtns);
            footerBorder.Child = footerGrid;
            Grid.SetRow(footerBorder, 3);
            root.Children.Add(footerBorder);

            this.Content = root;
            SelectTab(0);
        }

        public void SelectTab(int index)
        {
            for (int i = 0; i < 3; i++)
            {
                bool active = (i == index);
                pages[i].Visibility = active ? Visibility.Visible : Visibility.Collapsed;
                ModernUI.SetButtonColors(
                    navTabs[i],
                    active ? ModernUI.PrimaryBtnBg : ModernUI.SecondaryBtnBg,
                    active ? Brushes.White : ModernUI.TextMuted,
                    active ? ModernUI.PrimaryBtnBorder : ModernUI.SecondaryBtnBorder,
                    active ? FontWeights.SemiBold : FontWeights.Normal);
            }
        }

        private UIElement BuildColorPage()
        {
            StackPanel sp = new StackPanel();

            // Card 1: Presets
            UniformGrid presetGrid = new UniformGrid { Rows = 1, Columns = 5 };
            string[] presetNames = new string[] {
                "Padrão Natural (HDMI)",
                "Texto Nítido",
                "Vivo e Nítido",
                "Modo Leitura (Quente)",
                "Bordas Suaves"
            };
            for (int i = 0; i < presetNames.Length; i++)
            {
                int pIdx = i;
                Button b = ModernUI.CreateCustomButton(
                    presetNames[i],
                    new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                    ModernUI.TextPrimary,
                    ModernUI.SecondaryBtnBorder,
                    6,
                    new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                    33);
                b.FontSize = 12.2;
                b.Click += (s, e) => ApplyPreset(pIdx);
                presetGrid.Children.Add(b);
            }
            sp.Children.Add(ModernUI.CreateCard(
                "Predefinições Rápidas de Imagem (1-Clique)",
                "Selecione um perfil pronto ou personalize livremente as opções abaixo.",
                presetGrid));

            // Card 2: Color Space & Color Temp
            StackPanel spMatrix = new StackPanel();
            segColorMode = new SegmentedSelect(new string[] {
                "BT.709 Limitado",
                "BT.709 Completo",
                "BT.601 Limitado ★"
            });
            segColorTemp = new SegmentedSelect(new string[] {
                "Neutro (sem ganho)",
                "Quente suave",
                "Quente",
                "Frio suave"
            });
            spMatrix.Children.Add(ModernUI.CreateLabeledRow("Espaço de Cor HDMI:", segColorMode, false));
            spMatrix.Children.Add(ModernUI.CreateLabeledRow("Temperatura de Cor:", segColorTemp, true));
            sp.Children.Add(ModernUI.CreateCard(
                "Matriz de Cor HDMI e Temperatura (Balanço RGB)",
                "Comece em BT.601 limitado. A matriz/faixa precisa corresponder ao decodificador; compare com o padrão de teste.",
                spMatrix));

            // Card 3: Sliders
            StackPanel spSliders = new StackPanel();
            spSliders.Children.Add(CreateSliderRow("Brilho (Nível de Luz):", -30, 30, 0, out sldBrightness, out lblBrightnessVal,
                v => (v > 0 ? "+" : "") + Math.Round(v) + "%", false));
            spSliders.Children.Add(CreateSliderRow("Contraste (Ganho Dinâmico):", 60, 140, 100, out sldContrast, out lblContrastVal,
                v => Math.Round(v) + "%", false));
            spSliders.Children.Add(CreateSliderRow("Saturação (Intensidade de Cor):", 0, 160, 100, out sldSaturation, out lblSaturationVal,
                v => Math.Round(v) + "%", false));
            spSliders.Children.Add(CreateSliderRow("Correção Gamma (Meios-Tons):", 75, 140, 100, out sldGamma, out lblGammaVal,
                v => (v / 100.0).ToString("0.00", CultureInfo.InvariantCulture), true));

            TextBlock unusedLabel;
            spSliders.Children.Add(CreateSliderRow("Vivacidade Seletiva:", 0, 40, 0, out sldVibrance, out unusedLabel,
                v => Math.Round(v) + "%", false));
            spSliders.Children.Add(CreateSliderRow("Nitidez de Detalhes:", 0, 50, 0, out sldSharpness, out unusedLabel,
                v => Math.Round(v) + "%", false));
            spSliders.Children.Add(CreateSliderRow("Suavização de Bordas:", 0, 50, 0, out sldEdgeSmoothing, out unusedLabel,
                v => Math.Round(v) + "%", true));

            sp.Children.Add(ModernUI.CreateCard(
                "Ajuste Fino nos Pixel Shaders Direct3D 11 (Processado na GPU)",
                "Para serrilhado, teste Bordas Suaves. Nitidez alta pode acentuá-lo; zero desliga cada filtro.",
                spSliders));

            return sp;
        }

        private UIElement BuildPerfPage()
        {
            StackPanel sp = new StackPanel();
            segCaptureFps = new SegmentedSelect(new[] {"30 FPS — original", "45 FPS — teste", "60 FPS — teste"});
            sp.Children.Add(ModernUI.CreateCard("Alvo de Captura",
                "45/60 são experimentais. O valor solicitado não mede a taxa entregue pelo adaptador.", segCaptureFps));
            sp.Children.Add(ModernUI.CreateCard("Equilíbrio entre Fluidez e Definição", null,
                new TextBlock { Foreground=ModernUI.TextPrimary, TextWrapping=TextWrapping.Wrap, FontSize=14, LineHeight=23,
                    Text="30 FPS prioriza margem para compressão e processamento. 45/60 podem melhorar movimento, mas também aumentar uso de CPU/GPU e reduzir qualidade por quadro. Compare rolagem de texto e vídeo antes de confirmar.\n\nNitidez 15–25% é um ponto de partida. O filtro usa amostras extras somente no plano de luminosidade; em zero, essas leituras são removidas do shader. Não cria resolução extra nem recupera detalhes já perdidos na compressão." }));
            sp.Children.Add(ModernUI.CreateCard("O que foi preservado", null,
                new TextBlock { Foreground=ModernUI.TextMuted, TextWrapping=TextWrapping.Wrap, FontSize=13, LineHeight=22,
                    Text="Seleção automática de encoder, bitrate negociado com o adaptador, driver e firmware permanecem originais. Os antigos patches de threads/bitrate não foram reutilizados.\n\nPara texto: use a resolução nativa do monitor e evite espelhamento entre telas de resoluções diferentes. A subamostragem de cores 4:2:0 continua limitando bordas de texto colorido." }));
            return sp;
        }

        private UIElement BuildSystemPage()
        {
            StackPanel sp = new StackPanel();

            // Card 1: Quick Display Mode Actions
            UniformGrid dispGrid = new UniformGrid { Rows = 1, Columns = 3 };
            Button bExtend = ModernUI.CreateCustomButton("Estender Telas (Modo Estendido)", new SolidColorBrush(Color.FromRgb(15, 23, 42)), ModernUI.TextPrimary, ModernUI.SecondaryBtnBorder, 6, new Thickness(0, 0, 6, 0), 36);
            Button bClone = ModernUI.CreateCustomButton("Duplicar / Espelhar Vídeos", new SolidColorBrush(Color.FromRgb(15, 23, 42)), ModernUI.TextPrimary, ModernUI.SecondaryBtnBorder, 6, new Thickness(6, 0, 6, 0), 36);
            Button bWinSet = ModernUI.CreateCustomButton("Posição e Resolução (Windows)", new SolidColorBrush(Color.FromRgb(15, 23, 42)), ModernUI.TextPrimary, ModernUI.SecondaryBtnBorder, 6, new Thickness(6, 0, 0, 0), 36);

            bExtend.Click += (s, e) => SetWindowsDisplayMode("/extend", "Modo 'Estender Telas' ativado no Windows.");
            bClone.Click += (s, e) => SetWindowsDisplayMode("/clone", "Modo 'Duplicar / Espelhar Telas' ativado no Windows.");
            bWinSet.Click += (s, e) => { try { Process.Start("ms-settings:display"); } catch { } };

            dispGrid.Children.Add(bExtend);
            dispGrid.Children.Add(bClone);
            dispGrid.Children.Add(bWinSet);

            sp.Children.Add(ModernUI.CreateCard(
                "Modo de Exibição dos Monitores (Acesso Rápido)",
                "Alterne rapidamente entre Tela Estendida, Espelhamento ou organize a posição dos monitores no Windows.",
                dispGrid));

            // Card 2: Live Status & Bug Fixes
            StackPanel spStatus = new StackPanel();
            lblDiagAdapter = new TextBlock { Foreground = ModernUI.AccentGreen, FontSize = 13, Margin = new Thickness(0, 0, 0, 4) };
            lblDiagProcs = new TextBlock { Foreground = ModernUI.TextPrimary, FontSize = 13, Margin = new Thickness(0, 0, 0, 4) };
            lblDiagPatch = new TextBlock { Foreground = ModernUI.AccentBlue, FontSize = 13, Margin = new Thickness(0, 0, 0, 10) };

            TextBlock tbBugs = new TextBlock
            {
                Foreground = ModernUI.TextMuted,
                FontSize = 12.2,
                LineHeight = 19,
                Text = "A presença de processos não comprova a imagem no monitor.\n" +
                       "Após aplicar, confirme a imagem em até 25 segundos; caso contrário, o sistema reverte.\n" +
                       "Os ajustes de matriz/faixa são experimentais até a confirmação visual no adaptador."
            };

            spStatus.Children.Add(lblDiagAdapter);
            spStatus.Children.Add(lblDiagProcs);
            spStatus.Children.Add(lblDiagPatch);
            spStatus.Children.Add(tbBugs);

            sp.Children.Add(ModernUI.CreateCard(
                "Diagnóstico em Tempo Real e Correções Ativas",
                null,
                spStatus));

            // Card 3: Maintenance & Install on another PC
            UniformGrid maintGrid = new UniformGrid { Rows = 1, Columns = 3 };
            Button bRestart = ModernUI.CreateCustomButton("Reiniciar Monitor USB-C Agora", new SolidColorBrush(Color.FromRgb(20, 83, 60)), Brushes.White, new SolidColorBrush(Color.FromRgb(52, 211, 153)), 6, new Thickness(0, 0, 6, 0), 38);
            Button bInstall = ModernUI.CreateCustomButton("Instalar Driver + App neste PC", ModernUI.PrimaryBtnBg, Brushes.White, ModernUI.PrimaryBtnBorder, 6, new Thickness(6, 0, 6, 0), 38);
            Button bRestore = ModernUI.CreateCustomButton("Recuperar Original de Fábrica", new SolidColorBrush(Color.FromRgb(15, 23, 42)), ModernUI.TextPrimary, ModernUI.SecondaryBtnBorder, 6, new Thickness(6, 0, 0, 0), 38);

            bRestart.Click += (s, e) => RunWorker("--restart", null);
            bInstall.Content = "Abrir Padrão de Teste";
            bInstall.Click += (s, e) => CalibrationWindow.ShowPattern();
            bRestore.Click += (s, e) => RunWorker("--restore", null);

            maintGrid.Children.Add(bRestart);
            maintGrid.Children.Add(bInstall);
            maintGrid.Children.Add(bRestore);

            sp.Children.Add(ModernUI.CreateCard(
                "Recuperação e Verificação da Imagem",
                "Recuperar Original mantém o driver instalado e restaura somente o aplicativo de fábrica.",
                maintGrid));

            return sp;
        }

        private Grid CreateSliderRow(string title, double min, double max, double val, out Slider slider, out TextBlock valLabel, Func<double, string> formatter, bool isLast)
        {
            Grid g = new Grid { Margin = new Thickness(0, 0, 0, isLast ? 0 : 7) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });

            TextBlock lbl = new TextBlock
            {
                Text = title,
                Foreground = ModernUI.TextPrimary,
                FontSize = 12.8,
                VerticalAlignment = VerticalAlignment.Center
            };

            Slider sld = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = val,
                IsSnapToTickEnabled = true,
                TickFrequency = 1,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 12, 0),
                Cursor = Cursors.Hand
            };

            Border badge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                BorderBrush = ModernUI.BorderCard,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 3, 6, 3),
                VerticalAlignment = VerticalAlignment.Center
            };
            TextBlock vText = new TextBlock
            {
                Text = formatter(val),
                Foreground = ModernUI.AccentGreen,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            badge.Child = vText;

            TextBlock capturedLabel = vText;
            sld.ValueChanged += (s, e) => { capturedLabel.Text = formatter(sld.Value); };

            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(sld, 1);
            Grid.SetColumn(badge, 2);

            g.Children.Add(lbl);
            g.Children.Add(sld);
            g.Children.Add(badge);

            slider = sld;
            valLabel = vText;
            return g;
        }

        private void ApplyPreset(int preset)
        {
            int fps = new[] {30,45,60}[segCaptureFps.SelectedIndex];
            cfg = ImageProfiles.Create(preset,segColorMode.SelectedIndex,fps);
            LoadConfigToUI();
            string[] names = {"Natural", "Texto Nítido", "Vivo e Nítido", "Leitura", "Bordas Suaves"};
            lblFooter.Text="Perfil '"+names[preset]+"' selecionado. Clique em Aplicar para comparar.";
        }

        private void LoadConfigToUI()
        {
            segColorMode.SelectedIndex = cfg.ColorMode;
            segColorTemp.SelectedIndex = cfg.ColorTemp;
            sldBrightness.Value = cfg.Brightness;
            sldContrast.Value = cfg.Contrast;
            sldSaturation.Value = cfg.Saturation;
            sldGamma.Value = cfg.GammaX100;
            sldVibrance.Value = cfg.Vibrance;
            sldSharpness.Value = cfg.Sharpness;
            sldEdgeSmoothing.Value = cfg.EdgeSmoothing;
            segCaptureFps.SelectedIndex = cfg.CaptureFps==60 ? 2 : (cfg.CaptureFps==45 ? 1 : 0);

        }

        private void ReadUIToConfig()
        {
            cfg.ColorMode = segColorMode.SelectedIndex;
            cfg.ColorTemp = segColorTemp.SelectedIndex;
            cfg.Brightness = (int)Math.Round(sldBrightness.Value);
            cfg.Contrast = (int)Math.Round(sldContrast.Value);
            cfg.Saturation = (int)Math.Round(sldSaturation.Value);
            cfg.GammaX100 = (int)Math.Round(sldGamma.Value);
            cfg.Vibrance = (int)Math.Round(sldVibrance.Value);
            cfg.Sharpness = (int)Math.Round(sldSharpness.Value);
            cfg.EdgeSmoothing = (int)Math.Round(sldEdgeSmoothing.Value);
            cfg.CaptureFps = new[] {30,45,60}[segCaptureFps.SelectedIndex];

        }

        private void SaveAndApplyAll()
        {
            if (applying) return;
            try
            {
                ReadUIToConfig();
                string pending = Path.Combine(Path.GetTempPath(),"UDisplayPro-"+Guid.NewGuid().ToString("N")+".ini");
                cfg.Save(pending);
                RunWorker("--apply", pending);
            }
            catch(Exception ex) { MessageBox.Show(ex.Message,"Falha antes de aplicar",MessageBoxButton.OK,MessageBoxImage.Error); }
        }

        private async void RunWorker(string mode, string settings)
        {
            if (applying) return;
            applying=true;
            ((UIElement)this.Content).IsEnabled=false;
            lblFooter.Text="Atualizando com backup. Aguarde a confirmação da imagem...";
            try
            {
                if(mode=="--apply")
                {
                    lblFooter.Text="Validando ajustes e compilando shaders...";
                    await Task.Run(()=> {
                        ProConfig staged = new ProConfig(); staged.Load(settings);
                        SafePatcher.Build(SafePatcher.Original(EngineUpdate.OriginalPath),staged);
                    });
                }
                string args=mode+(settings==null ? "" : " \""+settings+"\"");
                using(Process worker=Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,args) {
                    UseShellExecute=true,Verb="runas",WorkingDirectory=appDir }))
                {
                    await Task.Run(()=>worker.WaitForExit());
                    if(worker.ExitCode==0) lblFooter.Text="Operação concluída. Confira a imagem do monitor.";
                    else if(worker.ExitCode==2) lblFooter.Text="Ajustes revertidos; versão anterior recuperada.";
                    else throw new IOException("A atualização falhou. Consulte "+EngineUpdate.LogPath);
                }
                UpdateLiveStatus();
            }
            catch(Exception ex) { lblFooter.Text="Operação não concluída."; MessageBox.Show(ex.Message,"UDisplay Pro",MessageBoxButton.OK,MessageBoxImage.Error); }
            finally
            {
                try { if(settings!=null && File.Exists(settings)) File.Delete(settings); }
                catch(IOException) { }
                catch(UnauthorizedAccessException) { }
                applying=false; ((UIElement)this.Content).IsEnabled=true;
            }
        }

        private void SetWindowsDisplayMode(string arg, string msg)
        {
            try { Process.Start(new ProcessStartInfo("DisplaySwitch.exe",arg) { UseShellExecute=true }); lblFooter.Text=msg; }
            catch(Exception ex) { MessageBox.Show(ex.Message,"Windows"); }
        }

        private void UpdateLiveStatus()
        {
            try
            {
                int count=0;
                foreach(Process p in Process.GetProcessesByName("UDisplay")) using(p) { count++; }
                lblDiagProcs.Text="• Processos UDisplay encontrados: "+count+" (não mede FPS nem confirma imagem)";
                lblDiagAdapter.Text="• Conexão do adaptador: verifique a imagem e as configurações de exibição do Windows.";
                lblDiagPatch.Text="• Cores/nitidez na GPU; alvo de captura ajustável. FPS real não medido.";
                lblHeaderBadge.Text=count>0 ? "● UDisplay em execução" : "● UDisplay parado";
            }
            catch(Exception ex) { lblHeaderBadge.Text="Diagnóstico indisponível"; lblDiagProcs.Text=ex.Message; }
        }

        public void ExportScreenshots(string outDir)
        {
            Directory.CreateDirectory(outDir);
            FrameworkElement rootEl = (FrameworkElement)this.Content;
            double w = 864;
            double h = 756;

            for (int t = 0; t < 3; t++)
            {
                SelectTab(t);
                rootEl.Measure(new Size(w, h));
                rootEl.Arrange(new Rect(0, 0, w, h));
                rootEl.UpdateLayout();

                RenderTargetBitmap rtb = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(rootEl);
                PngBitmapEncoder enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using (FileStream fs = new FileStream(Path.Combine(outDir, "tab_" + (t + 1) + ".png"), FileMode.Create))
                {
                    enc.Save(fs);
                }
            }
        }
    }

    static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0]=="--install")
            {
                try
                {
                    using(Process worker=Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,"--install-tray") {UseShellExecute=true,Verb="runas"}))
                    {
                        worker.WaitForExit();
                        if(worker.ExitCode!=0) {Environment.ExitCode=worker.ExitCode;return;}
                    }
                }
                catch(Exception ex) {Environment.ExitCode=1;MessageBox.Show(ex.Message,"Instalação do painel");return;}
            }
            if (args.Length > 0 && (args[0]=="--apply" || args[0]=="--restore" || args[0]=="--restart" || args[0]=="--recover" || args[0]=="--test-rollback" || args[0]=="--install-tray"))
            {
                try { Environment.ExitCode=EngineUpdate.Run(args[0],args.Length>1?args[1]:null); }
                catch(Exception ex) { Environment.ExitCode=1; MessageBox.Show(ex.Message+"\n\nLog: "+EngineUpdate.LogPath,"Falha de atualização",MessageBoxButton.OK,MessageBoxImage.Error); }
                return;
            }
            if (args.Length >= 3 && args[0]=="--build")
            {
                try {
                    if (string.Equals(Path.GetFullPath(args[1]),Path.GetFullPath(args[2]),StringComparison.OrdinalIgnoreCase))
                        throw new IOException("A saída não pode substituir o original.");
                    ProConfig cfg=new ProConfig(); if(args.Length>3)cfg.Load(args[3]);
                    File.WriteAllBytes(args[2],SafePatcher.Build(SafePatcher.Original(args[1]),cfg));
                } catch(Exception ex) { Environment.ExitCode=1; File.WriteAllText(args[2]+".error.txt",ex.ToString()); }
                return;
            }
            if (args != null && args.Length >= 2 && args[0] == "--screenshot")
            {
                MainWindow w = new MainWindow();
                w.ExportScreenshots(args[1]);
                return;
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Global\\UDisplayProConfiguratorSingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    foreach (Process p in Process.GetProcessesByName("UDisplayConfig")) using(p)
                    {
                        if (p.Id != Process.GetCurrentProcess().Id && p.MainWindowHandle != IntPtr.Zero)
                        {
                            ShowWindow(p.MainWindowHandle, 9); // SW_RESTORE
                            SetForegroundWindow(p.MainWindowHandle);
                            break;
                        }
                    }
                    return;
                }

                Application app = new Application();
                app.Run(new MainWindow());
            }
        }
    }
}

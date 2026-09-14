using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace Bcfier.Themes
{
  /// <summary>
  /// Frameless-окно SP: скругление через Clip (CornerRadius не режет детей),
  /// перетаскивание и ресайз через WM_NCHITTEST.
  /// Клип нельзя вешать на тот же элемент, что и DropShadowEffect.
  /// </summary>
  public static class SpWindowChrome
  {
    /// <summary>Радиус скругления окна, как SpRadius.Window.</summary>
    public const double WindowCornerRadius = 10.0;

    /// <summary>Толщина зоны ресайза по краям (DIP).</summary>
    public const double ResizeBorderThickness = 10.0;

    /// <summary>
    /// Клип элемента по скруглению. Меняется с размером.
    /// Не ставить на Border с Effect: тень рисует прямоугольный bitmap и съедает радиус.
    /// </summary>
    public static readonly DependencyProperty ClipRadiusProperty = DependencyProperty.RegisterAttached(
      "ClipRadius",
      typeof(double),
      typeof(SpWindowChrome),
      new PropertyMetadata(double.NaN, OnClipRadiusChanged));

    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;

    private static readonly ConditionalWeakTable<Window, object> EdgeResizeAttached =
      new ConditionalWeakTable<Window, object>();

    private static readonly SolidColorBrush HittableWindowBrush = CreateHittableBrush();

    /// <summary>Читает радиус клипа.</summary>
    public static double GetClipRadius(DependencyObject obj)
    {
      return obj != null ? (double)obj.GetValue(ClipRadiusProperty) : double.NaN;
    }

    /// <summary>Задаёт радиус клипа; 0 отключает скругление (развёрнутое окно).</summary>
    public static void SetClipRadius(DependencyObject obj, double value)
    {
      obj?.SetValue(ClipRadiusProperty, value);
    }

    /// <summary>Подписывает SizeChanged и сразу применяет клип.</summary>
    private static void OnClipRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
      var element = d as FrameworkElement;
      if (element == null)
        return;

      element.SizeChanged -= ClipRadius_SizeChanged;
      element.Loaded -= ClipRadius_Loaded;

      if (e.NewValue is double radius && !double.IsNaN(radius) && radius >= 0)
      {
        element.SizeChanged += ClipRadius_SizeChanged;
        element.Loaded += ClipRadius_Loaded;
        ClipToRoundedRect(element, radius);
      }
      else
      {
        element.Clip = null;
      }
    }

    /// <summary>Клип после первой раскладки, если SizeChanged пришёл с нулевым размером.</summary>
    private static void ClipRadius_Loaded(object sender, RoutedEventArgs e)
    {
      ApplyAttachedClip(sender as FrameworkElement);
    }

    /// <summary>Клип при любом изменении размера.</summary>
    private static void ClipRadius_SizeChanged(object sender, SizeChangedEventArgs e)
    {
      ApplyAttachedClip(sender as FrameworkElement);
    }

    /// <summary>Берёт ClipRadius с элемента и применяет геометрию.</summary>
    private static void ApplyAttachedClip(FrameworkElement element)
    {
      if (element == null)
        return;

      double radius = GetClipRadius(element);
      if (double.IsNaN(radius) || radius < 0)
        return;

      ClipToRoundedRect(element, radius);
    }

    /// <summary>Почти прозрачный, но hittable фон (alpha=1): иначе OS игнорирует края AllowsTransparency.</summary>
    private static SolidColorBrush CreateHittableBrush()
    {
      var brush = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
      brush.Freeze();
      return brush;
    }

    /// <summary>Подключает chrome и ресайз со всех сторон к frameless-окну.</summary>
    public static void Apply(Window window)
    {
      if (window == null)
        return;

      try
      {
        window.WindowStyle = WindowStyle.None;
        if (window.ResizeMode == ResizeMode.NoResize)
          window.ResizeMode = ResizeMode.CanResize;

        EnsureHittableBackground(window);

        try
        {
          WindowChrome.SetWindowChrome(window, null);
        }
        catch
        {
          // ignore
        }

        AttachEdgeResizeHook(window);
      }
      catch
      {
        // chrome не критичен
      }
    }

    /// <summary>Ставит hittable Background, если сейчас полностью прозрачный.</summary>
    public static void EnsureHittableBackground(Window window)
    {
      if (window == null)
        return;

      try
      {
        if (window.Background == null
            || Equals(window.Background, Brushes.Transparent)
            || (window.Background is SolidColorBrush scb && scb.Color.A == 0))
        {
          window.Background = HittableWindowBrush;
        }
      }
      catch
      {
        // ignore
      }
    }

    /// <summary>
    /// Клипает элемент по скруглённому прямоугольнику.
    /// CornerRadius у Border не обрезает детей, без Clip углы остаются прямыми.
    /// </summary>
    public static void ClipToRoundedRect(FrameworkElement element, double radius = WindowCornerRadius)
    {
      try
      {
        if (element == null)
          return;

        double width = element.ActualWidth;
        double height = element.ActualHeight;
        if (width <= 0 || height <= 0)
        {
          width = element.RenderSize.Width;
          height = element.RenderSize.Height;
        }

        if (width <= 0 || height <= 0)
          return;

        if (radius <= 0)
        {
          element.Clip = null;
          return;
        }

        var geometry = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
        geometry.Freeze();
        element.Clip = geometry;
      }
      catch
      {
        // клип не критичен
      }
    }

    /// <summary>Радиус 10 в нормальном режиме, 0 в развёрнутом (без прозрачных углов на весь экран).</summary>
    public static double GetWindowClipRadius(Window window)
    {
      if (window != null && window.WindowState == WindowState.Maximized)
        return 0;
      return WindowCornerRadius;
    }

    /// <summary>Перетаскивание окна за header.</summary>
    public static void DragMove(Window window)
    {
      try
      {
        if (window != null && Mouse.LeftButton == MouseButtonState.Pressed)
          window.DragMove();
      }
      catch
      {
        // ignore
      }
    }

    /// <summary>WM_NCHITTEST: ресайз за все края и углы.</summary>
    private static void AttachEdgeResizeHook(Window window)
    {
      if (EdgeResizeAttached.TryGetValue(window, out _))
        return;
      EdgeResizeAttached.Add(window, new object());

      void Attach()
      {
        try
        {
          var source = PresentationSource.FromVisual(window) as HwndSource;
          if (source == null)
          {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
              source = HwndSource.FromHwnd(handle);
          }

          source?.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            EdgeResizeWndProc(window, msg, lParam, ref handled));
        }
        catch
        {
          // ignore
        }
      }

      IntPtr hwnd = IntPtr.Zero;
      try
      {
        hwnd = new WindowInteropHelper(window).Handle;
      }
      catch
      {
        // ignore
      }

      if (hwnd != IntPtr.Zero || PresentationSource.FromVisual(window) is HwndSource)
        Attach();
      else
        window.SourceInitialized += (_, __) => Attach();
    }

    /// <summary>Определяет зону ресайза по краям и углам клиентской области.</summary>
    private static IntPtr EdgeResizeWndProc(Window window, int msg, IntPtr lParam, ref bool handled)
    {
      if (msg != WmNcHitTest || window.ResizeMode == ResizeMode.NoResize)
        return IntPtr.Zero;

      try
      {
        int xy = lParam.ToInt32();
        int x = (short)(xy & 0xFFFF);
        int y = (short)((xy >> 16) & 0xFFFF);
        Point clientPoint = window.PointFromScreen(new Point(x, y));

        double width = window.ActualWidth;
        double height = window.ActualHeight;
        if (width <= 0 || height <= 0)
          return IntPtr.Zero;

        const double slack = 4.0;
        if (clientPoint.X < -slack || clientPoint.Y < -slack
            || clientPoint.X > width + slack || clientPoint.Y > height + slack)
        {
          return IntPtr.Zero;
        }

        double left = Math.Max(0, clientPoint.X);
        double top = Math.Max(0, clientPoint.Y);
        double right = Math.Max(0, width - clientPoint.X);
        double bottom = Math.Max(0, height - clientPoint.Y);

        bool onLeft = left <= ResizeBorderThickness;
        bool onRight = right <= ResizeBorderThickness;
        bool onTop = top <= ResizeBorderThickness;
        bool onBottom = bottom <= ResizeBorderThickness;

        if (!(onLeft || onRight || onTop || onBottom))
          return IntPtr.Zero;

        IntPtr result;
        if (onTop && onLeft)
          result = (IntPtr)HtTopLeft;
        else if (onTop && onRight)
          result = (IntPtr)HtTopRight;
        else if (onBottom && onLeft)
          result = (IntPtr)HtBottomLeft;
        else if (onBottom && onRight)
          result = (IntPtr)HtBottomRight;
        else if (onTop)
          result = (IntPtr)HtTop;
        else if (onBottom)
          result = (IntPtr)HtBottom;
        else if (onLeft)
          result = (IntPtr)HtLeft;
        else if (onRight)
          result = (IntPtr)HtRight;
        else
          result = (IntPtr)HtClient;

        handled = true;
        return result;
      }
      catch
      {
        // ignore
      }

      return IntPtr.Zero;
    }
  }
}

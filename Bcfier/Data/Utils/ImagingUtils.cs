using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Bcfier.Data.Utils
{
  public static class ImagingUtils
  {
    /// <summary>Максимальный размер снимка BCF (рекомендация BCF 2.1).</summary>
    public const int BcfMaxPixelSize = 1500;

    /// <summary>Ширина миниатюры в списках замечаний, не для аннотирования.</summary>
    public const int ThumbnailPixelWidth = 480;

    /// <summary>
    /// Загружает снимок в полном размере, уменьшая только если больше BCF-лимита.
    /// Читает файл в память, чтобы после правки во внешнем редакторе не брался кэш WPF.
    /// </summary>
    public static ImageSource ImageSourceFromPath(string sourcePath)
    {
      try
      {
        byte[] imageBytes = LoadImageData(sourcePath);
        if (imageBytes == null || imageBytes.Length == 0)
          return null;

        BitmapImage image = CreateImage(imageBytes, decodePixelWidth: 0, decodePixelHeight: 0);
        if (image == null)
          return null;

        int width = image.PixelWidth;
        int height = image.PixelHeight;
        if (width <= 0 || height <= 0)
          return image;

        if (width <= BcfMaxPixelSize && height <= BcfMaxPixelSize)
          return image;

        double scale = Math.Max((double)width / BcfMaxPixelSize, (double)height / BcfMaxPixelSize);
        int decodeWidth = Convert.ToInt32(width / scale);
        return CreateImage(imageBytes, decodeWidth, 0);
      }
      catch (System.Exception ex1)
      {
        ExceptionUi.Show(ex1);
      }
      return null;
    }

    /// <summary>
    /// Сохраняет изображение; формат берётся из расширения файла.
    /// </summary>
    public static void SaveImageSource(ImageSource image, string destPath)
    {
      try
      {
        if (image == null || string.IsNullOrWhiteSpace(destPath))
          return;

        string directory = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(directory))
          Directory.CreateDirectory(directory);

        string ext = Path.GetExtension(destPath);
        if (string.IsNullOrWhiteSpace(ext))
          ext = ".png";

        byte[] imageBytes = GetEncodedImageData(image, ext);
        SaveImageData(imageBytes, destPath);
      }
      catch (System.Exception ex1)
      {
        ExceptionUi.Show(ex1);
      }
    }

    /// <summary>
    /// Ждёт, пока внешний редактор отпустит файл (актуально для mspaint на Win11).
    /// </summary>
    public static void WaitForExternalEditor(string filePath)
    {
      try
      {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
          return;

        const int pollMs = 250;
        const int openWaitMs = 8000;
        const int closeWaitMs = 30 * 60 * 1000;

        bool seenLock = false;
        int waited = 0;
        while (waited < openWaitMs)
        {
          if (IsFileLocked(filePath))
          {
            seenLock = true;
            break;
          }

          System.Threading.Thread.Sleep(pollMs);
          waited += pollMs;
        }

        // Классический Paint: WaitForExit уже дождался закрытия, блокировки не было.
        if (!seenLock)
          return;

        waited = 0;
        while (waited < closeWaitMs && IsFileLocked(filePath))
        {
          System.Threading.Thread.Sleep(pollMs);
          waited += pollMs;
        }
      }
      catch
      {
        // Не блокируем UI навсегда, если статус файла недоступен.
      }
    }

    /// <summary>true, если файл открыт другим процессом без доступа на запись.</summary>
    private static bool IsFileLocked(string filePath)
    {
      try
      {
        using (new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
          return false;
      }
      catch (IOException)
      {
        return true;
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Миниатюра для списка замечаний. Для аннотирования использовать ImageSourceFromPath.
    /// </summary>
    public static BitmapImage BitmapFromPath(string path)
    {
      return LoadBitmap(path, ThumbnailPixelWidth);
    }

    /// <summary>
    /// Загружает BitmapImage. decodePixelWidth=0 — исходный размер файла.
    /// </summary>
    private static BitmapImage LoadBitmap(string path, int decodePixelWidth)
    {
      try
      {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
          return null;

        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        if (decodePixelWidth > 0)
          image.DecodePixelWidth = decodePixelWidth;
        image.EndInit();
        if (image.CanFreeze)
          image.Freeze();
        return image;
      }
      catch
      {
        return null;
      }
    }
    /// <summary>
    /// Приводит изображение к 96 DPI с автоматическим ресайзом до BCF-лимита.
    /// </summary>
    public static BitmapSource ConvertBitmapTo96Dpi(BitmapImage bitmapImage)
    {
      try
      {
        double dpi = 96;
        int width = bitmapImage.PixelWidth;
        int height = bitmapImage.PixelHeight;

        if (width > BcfMaxPixelSize || height > BcfMaxPixelSize)
        {
          double scale = Math.Max((double)width / BcfMaxPixelSize, (double)height / BcfMaxPixelSize);
          width = Convert.ToInt32(width / scale);
          height = Convert.ToInt32(height / scale);
        }

        int stride = width * 4;
        byte[] pixelData = new byte[stride * height];
        bitmapImage.CopyPixels(pixelData, stride, 0);

        return BitmapSource.Create(width, height, dpi, dpi, PixelFormats.Bgra32, null, pixelData, stride);
      }
      catch (System.Exception ex1)
      {
        ExceptionUi.Show(ex1);
      }
      return null;
    }

    private static byte[] LoadImageData(string filePath)
    {
      try
      {
        FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        BinaryReader br = new BinaryReader(fs);
        byte[] imageBytes = br.ReadBytes((int)fs.Length);
        br.Close();
        fs.Close();
        return imageBytes;
      }
      catch (System.Exception ex1)
      {
        ExceptionUi.Show(ex1);
      }
      return null;
    }

    /// <summary>
    /// Создаёт BitmapImage из байтов файла.
    /// Не использовать IgnoreImageCache со StreamSource: у потока нет URI, WPF падает с ArgumentNullException.
    /// </summary>
    private static BitmapImage CreateImage(byte[] imageData, int decodePixelWidth, int decodePixelHeight)
    {
      try
      {
        if (imageData == null || imageData.Length == 0)
          return null;

        BitmapImage result = new BitmapImage();
        result.BeginInit();
        if (decodePixelWidth > 0)
          result.DecodePixelWidth = decodePixelWidth;
        if (decodePixelHeight > 0)
          result.DecodePixelHeight = decodePixelHeight;
        result.StreamSource = new MemoryStream(imageData);
        result.CreateOptions = BitmapCreateOptions.None;
        result.CacheOption = BitmapCacheOption.OnLoad;
        result.EndInit();
        if (result.CanFreeze)
          result.Freeze();
        return result;
      }
      catch (System.Exception ex1)
      {
        ExceptionUi.Show(ex1);
      }
      return null;
    }

    private static void SaveImageData(byte[] imageData, string filePath)
    {
      try
      {
        FileStream fs = new FileStream(filePath, FileMode.Create,
        FileAccess.Write);
        BinaryWriter bw = new BinaryWriter(fs);
        bw.Write(imageData);
        bw.Close();
        fs.Close();
      }
      catch (System.Exception ex1)
      {
        ExceptionUi.Show(ex1);
      }
    }

    private static byte[] GetEncodedImageData(ImageSource image, string preferredFormat)
    {
      try
      {
        byte[] result = null;
        BitmapEncoder encoder = null;
        switch (preferredFormat.ToLower())
        {
          case ".jpg":
          case ".jpeg":
            encoder = new JpegBitmapEncoder();
            break;
          case ".bmp":
            encoder = new BmpBitmapEncoder();
            break;
          case ".png":
            encoder = new PngBitmapEncoder();
            break;
          case ".tif":
          case ".tiff":
            encoder = new TiffBitmapEncoder();
            break;
          case ".gif":
            encoder = new GifBitmapEncoder();
            break;
          case ".wmp":
            encoder = new WmpBitmapEncoder();
            break;
        }
        if (image is BitmapSource)
        {
          MemoryStream stream = new MemoryStream();
          encoder.Frames.Add(BitmapFrame.Create(image as BitmapSource));
          encoder.Save(stream);
          stream.Seek(0, SeekOrigin.Begin);
          result = new byte[stream.Length];
          BinaryReader br = new BinaryReader(stream);
          br.Read(result, 0, (int)stream.Length);
          br.Close();
          stream.Close();
        }
        return result;
      }
      catch (System.Exception ex1)
      {
        ExceptionUi.Show(ex1);
      }
      return null;
    }
  }
}

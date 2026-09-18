namespace Bcfier.Data
{
  /// <summary>
  /// Выбор способа открытия viewpoint в Revit без зависимости от Revit API.
  /// </summary>
  public enum ViewpointOpenAction
  {
    /// <summary>Открыть найденный 2D/лист по SheetCamera.</summary>
    OpenSheetView = 0,

    /// <summary>Открыть рабочий 3D по Orthogonal/Perspective + ClippingPlanes.</summary>
    OpenOrthogonal3D = 1,

    /// <summary>SheetCamera есть, вида нет, стандартной камеры нет — уведомить пользователя.</summary>
    NotifySheetViewMissing = 2,

    /// <summary>Нет достаточных данных для открытия.</summary>
    None = 3
  }

  /// <summary>
  /// Приоритет открытия: Sheet (если найден) → Ortho/Perspective 3D → уведомление для листа/2D без камеры.
  /// </summary>
  public static class ViewpointOpenStrategy
  {
    /// <summary>
    /// Определяет действие при открытии viewpoint.
    /// </summary>
    public static ViewpointOpenAction Resolve(
      bool hasSheetCamera,
      bool sheetViewFound,
      bool hasOrthogonalCamera,
      bool hasPerspectiveCamera)
    {
      if (hasSheetCamera && sheetViewFound)
        return ViewpointOpenAction.OpenSheetView;

      if (hasOrthogonalCamera || hasPerspectiveCamera)
        return ViewpointOpenAction.OpenOrthogonal3D;

      if (hasSheetCamera)
        return ViewpointOpenAction.NotifySheetViewMissing;

      return ViewpointOpenAction.None;
    }
  }
}

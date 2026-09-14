using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Bcfier.Localization;

namespace Bcfier.Revit.Entry
{
  /// <summary>
  /// Интерактивный выбор элементов модели через PickObjects на потоке Revit API.
  /// </summary>
  public class ExtEvntPickModelElements : IExternalEventHandler
  {
    /// <summary>
    /// Текст подсказки в строке статуса Revit.
    /// </summary>
    public string Prompt { get; set; }

    /// <summary>
    /// Колбэк с результатом выбора (null при отмене или ошибке).
    /// </summary>
    public Action<IList<Reference>> Completed { get; set; }

    /// <summary>
    /// Выполняет PickObjects вне WPF-контекста, чтобы не ловить «Cannot re-enter the pick operation».
    /// </summary>
    public void Execute(UIApplication app)
    {
      IList<Reference> result = null;
      try
      {
        UIDocument uidoc = app?.ActiveUIDocument;
        if (uidoc != null)
        {
          string prompt = string.IsNullOrWhiteSpace(Prompt)
            ? Loc.SelectModelElementsPrompt
            : Prompt;

          result = uidoc.Selection.PickObjects(
            ObjectType.Element,
            new ModelElementSelectionFilter(),
            prompt);
        }
      }
      catch (Autodesk.Revit.Exceptions.OperationCanceledException)
      {
        result = null;
      }
      catch (Autodesk.Revit.Exceptions.InvalidOperationException)
      {
        // Повторный вход в pick или недоступный режим выбора
        result = null;
      }
      catch
      {
        result = null;
      }
      finally
      {
        Action<IList<Reference>> completed = Completed;
        Completed = null;
        Prompt = null;
        try
        {
          completed?.Invoke(result);
        }
        catch
        {
          // Ошибка UI-колбэка не должна ронять ExternalEvent
        }
      }
    }

    /// <summary>
    /// Имя обработчика для журнала Revit.
    /// </summary>
    public string GetName() => Loc.ProductName + " Pick Model Elements";

    /// <summary>
    /// Фильтр выбора: только элементы модели, без аннотаций и служебных объектов.
    /// </summary>
    private sealed class ModelElementSelectionFilter : ISelectionFilter
    {
      /// <summary>
      /// Разрешает выбор элемента, если у него есть модельная категория.
      /// </summary>
      public bool AllowElement(Element element)
      {
        try
        {
          return element?.Category != null
            && element.Category.CategoryType == CategoryType.Model;
        }
        catch
        {
          return false;
        }
      }

      /// <summary>
      /// Разрешает выбор по ссылке после прохождения фильтра элемента.
      /// </summary>
      public bool AllowReference(Reference reference, XYZ point)
      {
        return true;
      }
    }
  }
}

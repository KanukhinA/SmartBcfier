using System;
using System.Collections.Generic;
using System.Windows;
using Bcfier.Bcf.Bcf2;

namespace Bcfier.Data
{
    /// <summary>
    /// Хост для окна списка элементов (реализация задаётся Revit-модулем).
    /// </summary>
    public static class ComponentListHost
    {
        public static Func<Components, bool, Window> CreateWindow { get; set; }
        /// <summary>Выделяет элементы в модели. Второй аргумент: true — добавить к текущему выделению.</summary>
        public static Action<List<int>, bool> SelectInModel { get; set; }
        public static Func<Component, int?> ResolveElementId { get; set; }

        /// <summary>
        /// Запускает обновление ссылок компонентов в контексте Revit API (если задано хостом).
        /// </summary>
        public static Action<Action, IList<Component>> RunWithRevitContext { get; set; }

        /// <summary>
        /// Читает закэшированный ElementId без Revit API (задаётся Revit-хостом).
        /// </summary>
        public static Func<Component, (bool found, int? elementId)> TryGetCachedElementId { get; set; }

        /// <summary>
        /// Передаёт короткий статус загрузки в UI, если его поддерживает хост.
        /// </summary>
        public static Action<string> ReportProgress { get; set; }
    }
}

using System.ComponentModel;
using System.Xml.Serialization;

namespace Bcfier.Bcf.Bcf2
{
    /// <summary>
    /// Уведомления UI о смене списков статусов, типов, меток и ответственных.
    /// </summary>
    public partial class Topic : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Сообщает ComboBox, что ItemsSource нужно прочитать заново.
        /// Нужно для редактируемого списка ответственных: CollectionChanged ему недостаточно.
        /// </summary>
        public void NotifyDropdownCollectionsChanged()
        {
            try
            {
                PropertyChangedEventHandler handler = PropertyChanged;
                if (handler == null)
                    return;

                handler(this, new PropertyChangedEventArgs(nameof(TopicStatusesCollection)));
                handler(this, new PropertyChangedEventArgs(nameof(TopicTypesCollection)));
                handler(this, new PropertyChangedEventArgs(nameof(PrioritiesCollection)));
                handler(this, new PropertyChangedEventArgs(nameof(LabelsCollection)));
                handler(this, new PropertyChangedEventArgs(nameof(AssigneesCollection)));
            }
            catch
            {
                // Уведомление не должно ломать обновление списков
            }
        }
    }
}

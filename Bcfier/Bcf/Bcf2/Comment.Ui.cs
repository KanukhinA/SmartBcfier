using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using Bcfier.Data;

namespace Bcfier.Bcf.Bcf2
{
  /// <summary>
  /// UI helpers for BCF comments: change notification, soft-delete, edit.
  /// </summary>
  public partial class Comment : INotifyPropertyChanged
  {
    public const string DeletedStatus = "Deleted";

    private bool isEditingField;

    public event PropertyChangedEventHandler PropertyChanged;

    /// <summary>True when Status marks the comment as soft-deleted.</summary>
    [XmlIgnore]
    public bool IsDeleted =>
      string.Equals(statusField, DeletedStatus, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the comment text was edited after creation (messenger-style).</summary>
    [XmlIgnore]
    public bool IsEdited =>
      !IsDeleted
      && ModifiedDateSpecified
      && Math.Abs((ModifiedDate - Date).TotalSeconds) > 1;

    /// <summary>True when Author matches the current user name.</summary>
    [XmlIgnore]
    public bool IsMine => IsOwnedByCurrentUser();

    /// <summary>True when the current user may edit or soft-delete this comment.</summary>
    [XmlIgnore]
    public bool CanModify => !IsDeleted && IsOwnedByCurrentUser();

    /// <summary>Inline edit mode in the chat UI (not persisted).</summary>
    [XmlIgnore]
    public bool IsEditing
    {
      get => isEditingField;
      set
      {
        if (isEditingField == value)
          return;
        isEditingField = value;
        OnPropertyChanged();
      }
    }

    [XmlIgnore]
    public string EditBackup { get; private set; }

    public bool IsOwnedByCurrentUser()
    {
      string current = BcfAuthorContext.ResolveAuthor();
      if (string.IsNullOrWhiteSpace(authorField) || string.IsNullOrWhiteSpace(current))
        return false;
      return string.Equals(authorField.Trim(), current.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public void BeginEdit()
    {
      EditBackup = comment1Field;
      IsEditing = true;
    }

    public void CancelEdit()
    {
      Comment1 = EditBackup;
      IsEditing = false;
    }

    public void MarkDeleted(string by)
    {
      Status = DeletedStatus;
      Comment1 = string.Empty;
      ModifiedAuthor = by ?? BcfAuthorContext.ResolveAuthor();
      ModifiedDate = DateTime.UtcNow;
      ModifiedDateSpecified = true;
      IsEditing = false;
      NotifyOwnershipFlags();
    }

    public void ApplyEdit(string text, string by)
    {
      Comment1 = text ?? string.Empty;
      ModifiedAuthor = by ?? BcfAuthorContext.ResolveAuthor();
      ModifiedDate = DateTime.UtcNow;
      ModifiedDateSpecified = true;
      IsEditing = false;
      OnPropertyChanged(nameof(IsEdited));
    }

    protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void NotifyOwnershipFlags()
    {
      OnPropertyChanged(nameof(IsDeleted));
      OnPropertyChanged(nameof(IsEdited));
      OnPropertyChanged(nameof(IsMine));
      OnPropertyChanged(nameof(CanModify));
    }
  }
}

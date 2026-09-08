using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CpqSystemTool
{
    /// <summary>
    /// Office 组件卡片的数据项。对应 ComponentCatalog 中 10 个组件之一。
    /// </summary>
    public sealed class ComponentItem : INotifyPropertyChanged
    {
        public OfficeComponent Component { get; }
        public string DisplayName { get; }
        /// <summary>Icons 目录下的文件名（不含扩展名）</summary>
        public string IconFileName { get; }
        /// <summary>图标绝对路径，由 UserControl 的初始化逻辑填入</summary>
        public string IconPath { get; set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        public ComponentItem(OfficeComponent component, string displayName, string iconFileName)
        {
            Component = component;
            DisplayName = displayName;
            IconFileName = iconFileName;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

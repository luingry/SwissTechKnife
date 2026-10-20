using System;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;
using SwissTechKnife.Core;

namespace SwissTechKnife.Infrastructure
{
    /// <summary>Binding source for <see cref="Strings"/>: the indexer re-notifies when the language changes.</summary>
    public sealed class LocalizedStrings : INotifyPropertyChanged
    {
        private static readonly PropertyChangedEventArgs IndexerChanged = new PropertyChangedEventArgs(Binding.IndexerName);

        private LocalizedStrings()
        {
            Strings.LanguageChanged += (s, e) => PropertyChanged?.Invoke(this, IndexerChanged);
        }

        public static LocalizedStrings Instance { get; } = new LocalizedStrings();

        public event PropertyChangedEventHandler PropertyChanged;

        public string this[string key] => Strings.Get(key);
    }

    /// <summary>
    /// Localized text in XAML: <c>Text="{l:Loc SettingsTitle}"</c>. A one-way binding to <see cref="LocalizedStrings"/>,
    /// so it also works in setters and follows a language change without reloading the view.
    /// </summary>
    [MarkupExtensionReturnType(typeof(object))]
    public sealed class LocExtension : MarkupExtension
    {
        public LocExtension()
        {
        }

        public LocExtension(string key)
        {
            Key = key;
        }

        [ConstructorArgument("key")]
        public string Key { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var binding = new Binding("[" + Key + "]") { Source = LocalizedStrings.Instance, Mode = BindingMode.OneWay };
            return binding.ProvideValue(serviceProvider);
        }
    }
}

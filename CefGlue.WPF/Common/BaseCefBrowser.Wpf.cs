using System;
using System.Windows;
using System.Windows.Controls;

namespace Xilium.CefGlue.Common
{
    partial class BaseCefBrowser : ContentControl
    {
        public static readonly DependencyProperty AddressProperty = DependencyProperty.Register(
            nameof(Address),
            typeof(string),
            typeof(BaseCefBrowser),
            new PropertyMetadata(null, OnAddressChanged));

        private static void OnAddressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is BaseCefBrowser browser && e.NewValue is string address && browser._adapter.Address != address)
            {
                browser._adapter.Address = address;
            }
        }

        public partial string Address
        {
            get => (string)GetValue(AddressProperty);
            set => SetValue(AddressProperty, value);
        }

        protected void OnAdapterAddressChanged(object sender, string url)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if ((string)GetValue(AddressProperty) != url)
                {
                    SetCurrentValue(AddressProperty, url);
                }
            });
        }
    }
}

using System.Windows;

namespace Xilium.CefGlue.WPF;

public class DeviceScaleHelperFactory : IDeviceScaleHelper
{
    public static IDeviceScaleHelper Default { get; } = new DeviceScaleHelperFactory();
    public static IDeviceScaleHelper Custom { get; set; }
    public static IDeviceScaleHelper Current => Custom ?? Default;

    public float GetDeviceScaleFactor(PresentationSource source) => (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1d);
}

public interface IDeviceScaleHelper
{
    float GetDeviceScaleFactor(PresentationSource source);
}

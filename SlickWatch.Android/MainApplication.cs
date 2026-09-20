using Android.App;
using Android.Runtime;
using Avalonia.Android;

namespace SlickWatch.Android;

[Application]
public sealed class MainApplication(nint handle, JniHandleOwnership ownership)
    : AvaloniaAndroidApplication<PhoneApp>(handle, ownership)
{
    public override void OnCreate()
    {
        PhoneServices.Initialize(this);
        base.OnCreate();
    }
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => builder.WithInterFont();
}

using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace Siua.Common;

public abstract partial class PageBase(string displayName, MaterialIconKind icon,int index = 0) : ObservableValidator
{
    [ObservableProperty] private string _displayName = displayName;
    [ObservableProperty] private int _index = index;
    [ObservableProperty] private MaterialIconKind _icon = icon;

    public virtual Task OnPageLoadedAsync() => Task.CompletedTask;
}
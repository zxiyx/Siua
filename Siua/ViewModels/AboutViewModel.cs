using Material.Icons;
using Siua.Common;

namespace Siua.ViewModels;

/// <summary>提供关于页面的导航信息。</summary>
public partial class AboutViewModel :PageBase
{
    public AboutViewModel() : base("关于", MaterialIconKind.Information, 3)
    {
        
    }
}

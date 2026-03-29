using System.Threading.Tasks;
using Avalonia.Controls;

namespace Siua.Interfaces;

public interface IShowWindowManager
{
    void Show<TWindow, TViewModel>()
        where TWindow : Window, new()
        where TViewModel : class;

    Task ShowDialogAsync<TWindow, TViewModel>(Window owner)
        where TWindow : Window, new()
        where TViewModel : class;
    Task ShowDialogAsync<TWindow, TViewModel>()
        where TWindow : Window, new()
        where TViewModel : class;
}
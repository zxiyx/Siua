using System.Threading.Tasks;

namespace Siua.Interfaces;

public interface ICoreService
{

    public Task LoadPlaywright();

    public Task ParsePage();

}
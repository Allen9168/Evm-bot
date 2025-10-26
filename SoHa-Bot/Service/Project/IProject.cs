using SoHa_Bot.Model;
using System.Threading.Tasks;

namespace Soha.Service.Project
{
    public interface IProject
    {
        public Task StartAsync(ProjectConfig projectConfig);
        public Task UpdateAsync();
    }
}

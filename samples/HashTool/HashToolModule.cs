using System.Windows;
using SwissTechKnife.Sdk;

namespace SwissTechKnife.Samples.HashTool
{
    public sealed class HashToolModule : IModule
    {
        private IModuleContext _context;
        private HashView _view;

        public void Initialize(IModuleContext context) => _context = context;

        public FrameworkElement CreateView() => _view = new HashView(_context);

        public void Dispose()
        {
            _view?.Cancel();
            _view = null;
        }
    }
}

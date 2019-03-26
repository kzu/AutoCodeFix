using System;

namespace AutoCodeFix
{
    class DisposableAction : IDisposable
    {
        private Action action;

        public DisposableAction(Action action) => this.action = action;

        public void Dispose() => action();
    }
}

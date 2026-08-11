using System;

namespace AceLand.Lifecycle
{
    internal sealed class Disposable : IDisposable
    {
        public static readonly Disposable Empty = new Disposable(null);

        Action _action;
        public Disposable(Action action) => _action = action;

        public void Dispose()
        {
            var a = _action;
            _action = null;
            a?.Invoke();
        }
    }
}
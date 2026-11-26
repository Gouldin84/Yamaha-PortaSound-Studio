using System;
using Microsoft.AspNetCore.Components;
using PSS790Player.Services;

namespace PSS790Player.Components
{
    /// <summary>
    /// Base component that subscribes to targeted <see cref="AppStateStore"/> change scopes
    /// so any mounted workspace, modal, header, or sidebar updates immediately without polling.
    /// </summary>
    public abstract class StudioComponentBase : ComponentBase, IDisposable
    {
        private bool _disposed;

        [Inject]
        protected AppStateStore State { get; set; } = default!;

        protected virtual StateScope SubscribedScopes => StateScope.All;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            State.ScopedChanged += HandleScopedStateChanged;
        }

        private void HandleScopedStateChanged(StateScope scope)
        {
            if (_disposed) return;
            if ((scope & SubscribedScopes) != 0)
            {
                _ = InvokeAsync(StateHasChanged);
            }
        }

        protected virtual void OnDisposing() { }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            State.ScopedChanged -= HandleScopedStateChanged;
            OnDisposing();
        }
    }
}

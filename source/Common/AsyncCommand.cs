using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;

namespace PlayniteAchievements.Common
{
    public class AsyncCommand : CommandBase, ICommand
    {
        private readonly Func<object, Task> _executeAsync;
        private readonly Predicate<object> _canExecute;
        private bool _isExecuting;

        public AsyncCommand(Func<object, Task> executeAsync, Predicate<object> canExecute = null)
        {
            _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) =>
            !_isExecuting && (_canExecute?.Invoke(parameter) ?? true);

        public async void Execute(object parameter)
        {
            if (!CanExecute(parameter))
            {
                return;
            }

            try
            {
                _isExecuting = true;
                RaiseCanExecuteChanged();
                await _executeAsync(parameter).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is expected for some command paths; do not surface as unhandled.
            }
            catch (Exception ex)
            {
                try
                {
                    Trace.TraceError($"AsyncCommand execution failed: {ex}");
                }
                catch
                {
                    // Never let diagnostics throw from command execution.
                }
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        public event EventHandler CanExecuteChanged;

        public void RaiseCanExecuteChanged()
        {
            RaiseCanExecuteChangedOnUIThread(CanExecuteChanged);
        }
    }
}

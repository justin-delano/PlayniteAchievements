using System;
using System.Windows.Input;

namespace PlayniteAchievements.Common
{
    /// <summary>
    /// Simple relay command for synchronous operations.
    /// </summary>
    public class RelayCommand : CommandBase, ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object parameter) => _execute(parameter);

        public event EventHandler CanExecuteChanged;

        public void RaiseCanExecuteChanged()
        {
            RaiseCanExecuteChangedOnUIThread(CanExecuteChanged);
        }
    }
}

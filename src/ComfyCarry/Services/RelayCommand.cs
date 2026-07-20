using System.Windows.Input;

namespace ComfyCarry.Services;

/// <summary>
/// 极简 ICommand 实现，供托盘菜单命令用。
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _action;
    public RelayCommand(Action action) => _action = action;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _action();
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}

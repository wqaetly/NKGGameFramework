using NKGGameFramework.Runtime;

namespace NKGGameFramework.Tests.Runtime;

public sealed class MvvmTests
{
    [Fact]
    public void One_way_binding_updates_target_when_view_model_changes()
    {
        using var bindings = new BindingSet();
        var viewModel = new PlayerViewModel { Health = 10 };
        var target = new BindableValue<int>();

        bindings.Bind(viewModel, nameof(PlayerViewModel.Health), () => viewModel.Health, target);

        Assert.Equal(10, target.Value);

        viewModel.Health = 7;

        Assert.Equal(7, target.Value);

        bindings.Dispose();
        viewModel.Health = 3;

        Assert.Equal(7, target.Value);
    }

    [Fact]
    public void Two_way_binding_pushes_target_changes_back_to_view_model()
    {
        using var bindings = new BindingSet();
        var viewModel = new PlayerViewModel { Health = 10 };
        var target = new BindableValue<int>();

        bindings.Bind(
            viewModel,
            nameof(PlayerViewModel.Health),
            () => viewModel.Health,
            target,
            BindingMode.TwoWay,
            value => viewModel.Health = value);

        target.Value = 42;

        Assert.Equal(42, viewModel.Health);
    }

    private sealed class PlayerViewModel : ViewModelBase
    {
        private int _health;

        public int Health
        {
            get => _health;
            set => SetProperty(ref _health, value);
        }
    }
}

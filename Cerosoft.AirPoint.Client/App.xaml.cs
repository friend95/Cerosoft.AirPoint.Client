namespace Cerosoft.AirPoint.Client
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        // FIX: Override CreateWindow instead of setting MainPage (Modern .NET MAUI Standard)
        protected override Window CreateWindow(IActivationState? activationState)
        {
            // Set HomePage as the start screen inside a NavigationPage
            return new Window(new NavigationPage(new HomePage()));
        }
    }
}
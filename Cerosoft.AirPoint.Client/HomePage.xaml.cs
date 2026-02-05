namespace Cerosoft.AirPoint.Client
{
    public partial class HomePage : ContentPage
    {
        public HomePage()
        {
            InitializeComponent();
        }

        private async void OnWifiClicked(object sender, EventArgs e)
        {
            if (sender is VisualElement card)
            {
                // FIX: Replaced obsolete 'ScaleTo' with 'ScaleToAsync'
                await card.ScaleToAsync(0.95, 100);
                await card.ScaleToAsync(1.0, 100);
            }
            await Navigation.PushAsync(new MainPage(isWifi: true));
        }

        private async void OnBluetoothClicked(object sender, EventArgs e)
        {
            if (sender is VisualElement card)
            {
                // FIX: Replaced obsolete 'ScaleTo' with 'ScaleToAsync'
                await card.ScaleToAsync(0.95, 100);
                await card.ScaleToAsync(1.0, 100);
            }
            await Navigation.PushAsync(new MainPage(isWifi: false));
        }
    }
}
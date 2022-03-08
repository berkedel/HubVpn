using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DotRas;
using OtpNet;

namespace HubVpn
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        // ReSharper disable once MemberCanBePrivate.Global
        public CollectionView CbVpnNames { get; set; }

        // ReSharper disable once MemberCanBePrivate.Global
        public ComboBoxItem CbSelectedVpnName { get; set; }

        private readonly RasDialer _dialer;
        private readonly RasConnectionWatcher _watcher;
        private RasConnection _connection;
        private bool _connected;
        private readonly Totp _otp;
        private const string Username = "YOUR_USER_NAME";
        private const string Seed = "YOUR_TOTP_TOKEN";
        private const string Pattern = @"\[(.*?)\]";

        public MainWindow()
        {
            InitializeComponent();

            DataContext = this;

            var secret = Base32Encoding.ToBytes(Seed.ToUpper());
            _otp = new Totp(secret);

            _dialer = new RasDialer();
            var vpnAdapterPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Network\Connections\Pbk\rasphone.pbk"
            );

            Dispatcher.Invoke(() =>
            {
                var matches = Regex.Matches(File.ReadAllText(vpnAdapterPath), Pattern);
                var availableVpnNames =
                    (from Match match in matches select new ComboBoxItem {Content = match.Groups[1].ToString()})
                    .ToList();
                CbVpnNames = new CollectionView(availableVpnNames);
                CbVpnNames.MoveCurrentTo(availableVpnNames[0]);
                CbVpnNames.CurrentChanged += CbVpnNames_CurrentChanged;
            });

            _dialer.PhoneBookPath = vpnAdapterPath;

            _watcher = new RasConnectionWatcher();
            _watcher.Connected += OnConnectionConnected;
            _watcher.Disconnected += OnConnectionDisconnected;
        }

        private void CbVpnNames_CurrentChanged(object sender, EventArgs e)
        {
            CbSelectedVpnName = (CbVpnNames.CurrentItem as ComboBoxItem);
        }

        private void OnConnectionDisconnected(object sender, RasConnectionEventArgs e)
        {
            _connected = false;
            Dispatcher.Invoke(() =>
            {
                BtnConnect.IsEnabled = true;
                BtnConnect.Content = "Connect";
            });
        }

        private void OnConnectionConnected(object sender, RasConnectionEventArgs e)
        {
            _connected = true;
            Dispatcher.Invoke(() =>
            {
                BtnConnect.IsEnabled = true;
                BtnConnect.Content = "Disconnect";
            });
        }

        private async void BtnConnect_OnClick(object sender, RoutedEventArgs e)
        {
            if (!_connected)
            {
                Dispatcher.Invoke(() =>
                {
                    BtnConnect.IsEnabled = false;
                    BtnConnect.Content = "Connecting...";
                });
                await ConnectVpn();
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    BtnConnect.IsEnabled = false;
                    BtnConnect.Content = "Disconnecting...";
                });
                await DisconnectVpn();
            }
        }

        private void MainWindow_OnClosing(object sender, CancelEventArgs e)
        {
            if (_watcher.IsActive)
            {
                _watcher.Stop();
            }
        }

        private async Task ConnectVpn()
        {
            _dialer.EntryName = CbSelectedVpnName.Content.ToString();
            var otpCode = _otp.ComputeTotp();

            _dialer.Credentials = new NetworkCredential(Username, otpCode);

            _watcher.Start();

            _connection = await _dialer.ConnectAsync();
            _watcher.Connection = _connection;
        }

        private async Task DisconnectVpn()
        {
            await _connection.DisconnectAsync(CancellationToken.None);
            _watcher.Stop();
        }
    }
}
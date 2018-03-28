using System;
using System.Collections.ObjectModel;
using System.Diagnostics;

using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.UI.Xaml;

//using Windows.ApplicationModel.Store;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Collections.Generic;
using Windows.Storage.Streams;
using System.Text;

namespace GattInformer
{
    public partial class MainPage
    {
        #region Variables

        private BluetoothLEDevice device;
        private BluetoothLEAdvertisementWatcher BleWatcher;
        private DeviceWatcher deviceWatcher;

        string deviceFilterString = "e";

        private List<string> spottedDevices = new List<string>();
        private ObservableCollection<String> deviceList = new ObservableCollection<String>();
        private List<GattDeviceService> myServices = new List<GattDeviceService>();
        private IReadOnlyList<GattCharacteristic> myCharacs = new List<GattCharacteristic>();

        private string _txtSpotted;
        private string _txtServices;
        private string _txtCharacs;
        private string _txtUpdate;

        private GattDeviceServicesResult gatt = null;
        private GattDeviceService myDataService;
        private GattCharacteristic IMUdataChar;
        float[] currentIMUData;

        private const GattClientCharacteristicConfigurationDescriptorValue _notify = GattClientCharacteristicConfigurationDescriptorValue.Notify;
        private const GattClientCharacteristicConfigurationDescriptorValue _unnotify = GattClientCharacteristicConfigurationDescriptorValue.None;

        private DispatcherTimer dispatcherTimer = new DispatcherTimer();
        private double captureDuration = 0;

        #endregion Variables


        #region XAML UI Bindings

        // Bindings for XAML buttons
        private void btnFilterDevices_Click(object sender, RoutedEventArgs e)
        { UpdateDeviceWatcher(); }

        private void txtToFind_GotFocus(object sender, RoutedEventArgs e)
        { txtToFind.SelectAll(); }

        private void GetServices_Click(object sender, RoutedEventArgs e)
        { GetServices(); }

        private void btnStopDataStream_Click(object sender, RoutedEventArgs e)
        { StopDataStreaming(); }

        private void btnStartDataStream_Click(object sender, RoutedEventArgs e)
        { StreamSensorHubData(); }

        private void btnDisconnect_Click(object sender, RoutedEventArgs e)
        { Disconnect(); }

        private void btnClearUpdate_Click(object sender, RoutedEventArgs e)
        { txtUpdate.Text = ""; }

        #endregion XAML UI Bindings


        #region Timer

        private void dispatcherTimer_Tick(object sender, object e)
        {
            Update_Timer();
        }

        private async void Update_Timer()
        {
            await this.Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        captureDuration += dispatcherTimer.Interval.TotalMilliseconds / 1000;
                        txtTimer.Text = captureDuration.ToString();
                    });
        }

        #endregion Timer


        public MainPage()
        {
            this.InitializeComponent();

            deviceList.Clear();

            dispatcherTimer.Tick += dispatcherTimer_Tick;
            dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, 200);

            // Create and initialise watchers
            SetupDeviceWatcher();
            SetupAdvertWatcher();
            StartScanning();
        }

        public Task<bool> UiUpdate()
        {
            var tcs = new TaskCompletionSource<bool>();
            Task.Run(async () =>
            {
                await this.Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        txtSpotted.Text = _txtSpotted ?? "";
                        txtServices.Text = _txtServices ?? "";
                        txtCharacs.Text = _txtCharacs ?? "";
                        txtUpdate.Text = _txtUpdate ?? "";
                    });
            });
            return tcs.Task;
        }


        #region Setup Watchers, Handle Advert Data

        private void SetupDeviceWatcher()
        {
            string myAqsFilter = "System.ItemNameDisplay:~~\"" + deviceFilterString;

            string[] aepProperies = new string[] {
                "System.ItemNameDisplay",
                "System.Devices.Aep.DeviceAddress",
                "System.Devices.Aep.IsConnected",
                "System.Devices.Aep.IsPresent"
            };

            // Device Watcher
            deviceWatcher = DeviceInformation.CreateWatcher(
                myAqsFilter, aepProperies,
                DeviceInformationKind.AssociationEndpoint);

            deviceWatcher.Added += DeviceWatcher_Added;
            deviceWatcher.Updated += DeviceWatcher_Updated;
            deviceWatcher.Removed += DeviceWatcher_Removed;
        }


        private void SetupAdvertWatcher() {

            // BLE Advert Watcher
            BleWatcher = new BluetoothLEAdvertisementWatcher
            { ScanningMode = BluetoothLEScanningMode.Active };
            //BleWatcher.SignalStrengthFilter.InRangeThresholdInDBm = -70;
            BleWatcher.SignalStrengthFilter.OutOfRangeTimeout = TimeSpan.FromMilliseconds(1000);

            BleWatcher.Received += WatcherOnReceived;
            BleWatcher.Stopped += Watcher_Stopped;
        }

        private void StartScanning()
        {
            BleWatcher.Start();
            deviceWatcher.Start();
        }

        

        private void HandleWatcher(BluetoothLEAdvertisementReceivedEventArgs watcherArgs)
        {
            string _name = watcherArgs.Advertisement.LocalName;
            var btAddr = watcherArgs.BluetoothAddress.ToString();

            if (_name.Length == 0)
            {
                _name = "? " + btAddr;
            }

            if (!spottedDevices.Contains(_name))
            {
                spottedDevices.Add(_name);
                _txtSpotted = string.Join(Environment.NewLine, spottedDevices);
            }

            /* snooping on data sections
            List<byte[]> dataSections = new List<byte[]>();
            foreach (BluetoothLEAdvertisementDataSection ds in watcherArgs.Advertisement.DataSections)
            {
                dataSections.Add(readFromBuffer(ds.Data));
                Debug.WriteLine("<" + btAddr + ">\t" + ByteArrayToString(dataSections.Last()));
            }
            */
            UiUpdate();
        }

        #endregion Setup Watchers, Handle Advert Data


        #region Device Handling

        private async void DisplayDeviceInfo(BluetoothLEDevice dev)
        {
            await this.Dispatcher.RunAsync(
                Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    txtDevice.Text = dev.Name.ToString();
                    txtBTAddr.Text = dev.BluetoothAddress.ToString();
                    btnGetServices.IsEnabled = true;
                });

        }

        #endregion Device Handling


        #region Service & Characteristic Handling

        private async void GetServices()
        {
            gatt = await device.GetGattServicesAsync();

            if (gatt != null)
            {
                int serviceCount = gatt.Services.Count;
                txtDevConnStat.Text = device.ConnectionStatus.ToString();
                txtServCnt.Text = serviceCount.ToString();
                txtServStat.Text = gatt.Status.ToString();

                List<string> serviceUuids = new List<string>();

                myServices.Clear();
                foreach (GattDeviceService gds in gatt.Services)
                {
                    myServices.Add(gds);
                    serviceUuids.Add(gds.AttributeHandle.ToString() + ": " + gds.Uuid.ToString());
                }
                _txtServices = string.Join(Environment.NewLine, serviceUuids);

                if (myServices.Count > 0)
                {
                    myDataService = myServices.Single(s => s.Uuid == new Guid("6E400001-B5A3-F393-E0A9-E50E24DCCA9E"));
                }

                if (gatt.Status == GattCommunicationStatus.Success)
                {
                    txtServStat.Text = "Found and Ready";

                    var resres = await myDataService.GetCharacteristicsAsync();
                    myCharacs = resres.Characteristics;

                    // update chracteristic list
                    List<string> charUuids = new List<string>();
                    foreach (GattCharacteristic ch in myCharacs)
                    {
                        charUuids.Add(ch.Uuid.ToString());
                    }
                    _txtCharacs = string.Join(Environment.NewLine, charUuids);

                    //update connection status
                    txtDevConnStat.Text = device.ConnectionStatus.ToString();
                }
                UiUpdate();
            }
        }


        #endregion Service & Characteristic Handling


        #region Data Handling

        private async void StopDataStreaming()
        {
            if (IMUdataChar != null)
            {
                await enableNotifications(IMUdataChar, _unnotify);
                dispatcherTimer.Stop();
            }
        }

        private async void StreamSensorHubData()
        {
            Guid HubTx = new Guid("6E400003-B5A3-F393-E0A9-E50E24DCCA9E");

            // Subscribe to Hub Data Characteristic

            IMUdataChar = myCharacs.Single(c => c.Uuid == HubTx);
            if (IMUdataChar.Service != null)
            {
                await enableNotifications(IMUdataChar, _notify);
                IMUdataChar.ValueChanged += GotIMUData;
                dispatcherTimer.Start();
            }
        }

        private void GotIMUData(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            byte[] newData = readFromBuffer(args.CharacteristicValue);
            currentIMUData = new float[newData.Length / 4];
            System.Buffer.BlockCopy(newData, 0, currentIMUData, 0, newData.Length);

            _txtUpdate = string.Join(Environment.NewLine, currentIMUData);
            UiUpdate();
        }

        private byte[] readFromBuffer(IBuffer data)
        {
            DataReader _reader = DataReader.FromBuffer(data);
            byte[] fileContent = new byte[_reader.UnconsumedBufferLength];
            _reader.ReadBytes(fileContent);
            return fileContent;
        }

        public static String ByteArrayToString(byte[] ba)
        {
            StringBuilder hex = new StringBuilder(ba.Length * 2);
            foreach (byte b in ba)
            { hex.Append(b + " "); }
            return hex.ToString();
        }


        #endregion Data Handling


        #region Watcher Functions

        private async void WatcherOnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            // Check advert properties and snoop a little bit
            if (args.Advertisement == null)
            {
                return;
            }
            else
            {
                HandleWatcher(args);
            }
            
            // Update device info based on filter
            if (device == null)
            {
                device = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);
            }
            else
            {
                if (deviceList.Contains(device.DeviceId))
                {
                    DisplayDeviceInfo(device);
                }
                else
                {
                    device = null;
                }
            } 
        }


        private void Watcher_Stopped(BluetoothLEAdvertisementWatcher watcher, BluetoothLEAdvertisementWatcherStoppedEventArgs eventArgs)
        {
            Debug.WriteLine($"Watcher stopped, check that bluetooth is switched on.");
        }


        private void UpdateDeviceWatcher()
        {
            deviceList.Clear();
            deviceFilterString = txtToFind.Text;

            // Stop, destroy, create and start
            deviceWatcher.Stop();
            deviceWatcher = null;
            SetupDeviceWatcher();
            deviceWatcher.Start();
        }


        private void DeviceWatcher_Updated(DeviceWatcher watcher, DeviceInformationUpdate update)
        {
            List<String> keys = update.Properties.Keys.ToList();
            List<Object> vals = update.Properties.Values.ToList();
            List<string> properties = new List<string>();

            for (int x = 0; x < keys.Count(); x++)
            {
                properties.Add($"{x}: {keys[x].ToString()}: {vals[x].ToString()}");
            }

            _txtUpdate = string.Join(Environment.NewLine, properties);
            //UiUpdate();
        }


        private void DeviceWatcher_Removed(DeviceWatcher watcher, DeviceInformationUpdate args)
        {
        }


        private void DeviceWatcher_Added(DeviceWatcher watcher, DeviceInformation args)
        {
            if (!deviceList.Contains(args.Id))
            {
                deviceList.Add(args.Id);
                Debug.WriteLine(args.Name + "'s ID added to device list");
            }
        }

        #endregion Watcher Functions


        #region Read, Write and Notify

        public async Task<byte[]> read(GattCharacteristic characteristic)
        {
            byte[] response = (await characteristic.ReadValueAsync()).Value.ToArray();
            return response;
        }

        public async Task<GattCommunicationStatus> write(GattCharacteristic characteristic, byte[] data)
        {
            return await characteristic.WriteValueAsync(data.AsBuffer(), GattWriteOption.WriteWithoutResponse);
        }

        public async Task<GattCommunicationStatus> enableNotifications(GattCharacteristic characteristic, GattClientCharacteristicConfigurationDescriptorValue value)
        {
            GattCommunicationStatus status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(value);
            return status;
        }


        #endregion Read, Write and Notify


        private void Disconnect()
        {
            spottedDevices.Clear();

            #region Clear GUI Elements

            txtDevice.Text = "...";
            txtBTAddr.Text = "";
            txtDevConnStat.Text = "";
            txtServCnt.Text = "";
            txtServStat.Text = "";

            txtSpotted.Text = "";
            txtServices.Text = "";
            txtCharacs.Text = "";

            txtUpdate.Text = "Attempting disconnection...";

            btnGetServices.IsEnabled = false;

            #endregion Clear GUI Elements

            if (IMUdataChar != null)
            {
                IMUdataChar = null;
            }
            //IMUdataChar = IMUdataChar ?? null;

            if (gatt != null)
            {
                myDataService.Dispose();
                gatt = null;
            }

            if (device != null)
            {
                device.Dispose();
                device = null;
            }

            GC.Collect();
        }

        public void CloseApplication()
        {
            deviceWatcher.Added -= DeviceWatcher_Added;
            deviceWatcher.Updated -= DeviceWatcher_Updated;
            deviceWatcher.Removed -= DeviceWatcher_Removed;

            BleWatcher.Received -= WatcherOnReceived;
            BleWatcher.Stopped -= Watcher_Stopped;

            Debug.WriteLine("stopping watchers...");
            BleWatcher.Stop();
            deviceWatcher.Stop();
        }

    }
}



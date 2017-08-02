using System;
using System.Collections.ObjectModel;
using System.Diagnostics;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

//using Windows.ApplicationModel.Store;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Collections.Generic;

namespace BluAdvert
{

    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public partial class MainPage : Page
    {
        private BluetoothLEDevice device;
        public BluetoothLEAdvertisementWatcher BleWatcher;
        public DeviceWatcher deviceWatcher;
        ObservableCollection<String> deviceList = new ObservableCollection<String>();

        private const GattClientCharacteristicConfigurationDescriptorValue CHARACTERISTIC_NOTIFICATION_TYPE = GattClientCharacteristicConfigurationDescriptorValue.Notify;
        private GattCommunicationStatus emgNotifierConnStatus = GattCommunicationStatus.Unreachable;

        private GattDeviceServicesResult gatt;
        private GattCharacteristic selectedCharacteristic;
        private GattCharacteristic commandCharacteristic;

        private DispatcherTimer dispatcherTimer = new DispatcherTimer();
        private double captureDuration = 0;



        // Myo-specific Guids
        Guid MYO_SERVICE_GCS = new Guid("D5060001-A904-DEB9-4748-2C7F4A124842"); // Control Service
        Guid MYO_FIRMWARE_CH = new Guid("D5060201-A904-DEB9-4748-2C7F4A124842"); // Firmware Version Characteristic
        Guid MYO_DEVICE_NAME = new Guid("D5062A00-A904-DEB9-4748-2C7F4A124842"); // Device Name
        Guid BATTERY_SERVICE = new Guid("0000180f-0000-1000-8000-00805f9b34fb"); // Battery Service
        Guid BATTERY_LEVEL_C = new Guid("00002a19-0000-1000-8000-00805f9b34fb"); // Battery Level Characteristic
        Guid COMMAND_CHARACT = new Guid("D5060401-A904-DEB9-4748-2C7F4A124842"); // Command Characteristic (write)


        public enum myohw_guids
        {
            ControlService = 0x0001,        ///< Myo info service
            MyoInfoCharacteristic = 0x0101, ///< Serial number for this Myo and various parameters which
                                            ///< are specific to this firmware. Read-only attribute. 
                                            ///< See myohw_fw_info_t.
                                            
            FirmwareVersionCharacteristic = 0x0201, ///< Current firmware version. Read-only characteristic.
                                                    ///< See myohw_fw_version_t.
                                                    ///

            CommandCharacteristic = 0x0401, ///< Issue commands to the Myo. Write-only characteristic.
                                            ///< See myohw_command_t.

            ImuDataService = 0x0002, ///< IMU service
            IMUDataCharacteristic = 0x0402, ///< See myohw_imu_data_t. Notify-only characteristic.
            MotionEventCharacteristic = 0x0502, ///< Motion event data. Indicate-only characteristic.

            ClassifierService = 0x0003, ///< Classifier event service.
            ClassifierEventCharacteristic = 0x0103, ///< Classifier event data. Indicate-only characteristic. See myohw_pose_t.

            EmgDataService = 0x0005, ///< Raw EMG data service.
            EmgData0Characteristic = 0x0105, ///< Raw EMG data. Notify-only characteristic.
            EmgData1Characteristic = 0x0205, ///< Raw EMG data. Notify-only characteristic.
            EmgData2Characteristic = 0x0305, ///< Raw EMG data. Notify-only characteristic.
            EmgData3Characteristic = 0x0405, ///< Raw EMG data. Notify-only characteristic.
        }

        // Bindings for XAML buttons
        private void GetServices_Click(object sender, RoutedEventArgs e)
        { getDeviceInfo(); }

        private void btnStopEMGStream_Click(object sender, RoutedEventArgs e)
        { Stop_EMG_Data(); }

        private void btnStreamEMG_Click(object sender, RoutedEventArgs e)
        { Stream_EMG_Data(); }

        private void btnNotifyEMGChars_Click(object sender, RoutedEventArgs e)
        { Init_EMG_Channels(); }

        private void btnDisconnect_Click(object sender, RoutedEventArgs e)
        { Disconnect_Myo(); }

        private void EllEMG0_LayoutUpdated(object sender, object e)
        { Update_Ellipse(); }

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
                        txtArrLen.Text = emgCh1.Count.ToString();
                    });
        }

        public MainPage()
        {
            this.InitializeComponent();
            
            dispatcherTimer.Tick += dispatcherTimer_Tick;
            dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, 200);

            ellEMG0.LayoutUpdated += EllEMG0_LayoutUpdated;

            deviceWatcher = DeviceInformation.CreateWatcher(
                "System.ItemNameDisplay:~~\"Myo\"",
                new string[] {
            "System.Devices.Aep.DeviceAddress",
            "System.Devices.Aep.IsConnected" },
                DeviceInformationKind.AssociationEndpoint);
            deviceWatcher.Added += DeviceWatcher_Added;
            deviceWatcher.Updated += DeviceWatcher_Updated;
            deviceWatcher.Removed += DeviceWatcher_Removed;

            BleWatcher = new BluetoothLEAdvertisementWatcher
            { ScanningMode = BluetoothLEScanningMode.Active };
            BleWatcher.SignalStrengthFilter.InRangeThresholdInDBm = -70;
            BleWatcher.SignalStrengthFilter.OutOfRangeTimeout = TimeSpan.FromMilliseconds(1000);

            BleWatcher.Received += WatcherOnReceived;
            BleWatcher.Stopped += Watcher_Stopped;

            deviceList.Clear();
            StartScanning();
        }

        private void StartScanning()
        {
            BleWatcher.Start();
            deviceWatcher.Start();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
        }


        async private void WatcherOnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            device = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);

            if (device != null)
            {
                if (deviceList.Contains(device.Name))
                {
                    await this.Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        txtDevice.Text = device.Name.ToString();
                        txtBTAddr.Text = args.BluetoothAddress.ToString();
                        btnGetServices.IsEnabled = true;
                        Debug.WriteLine("starting...");
                    });
                }
            }
        }

        

        private async void getDeviceInfo()
        {
            device = await BluetoothLEDevice.FromBluetoothAddressAsync(Convert.ToUInt64(txtBTAddr.Text));
            if (device != null)
            {
                if (deviceList.Contains(device.Name))
                {
                    gatt = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached);

                    int serviceCount = gatt.Services.Count;
                    txtDevConnStat.Text = gatt.Status.ToString();
                    txtServCnt.Text = serviceCount.ToString();
                    List<string> outie = new List<string>();

                    if (gatt.Status == GattCommunicationStatus.Success)
                    {
                        var characs = await gatt.Services.Single(s => s.Uuid == MYO_SERVICE_GCS).GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                        txtServStat.Text = characs.Status.ToString();

                        foreach (GattCharacteristic ch in characs.Characteristics)
                        {
                            outie.Add(ch.AttributeHandle.ToString());      
                        }

                        string attHandles = string.Join(", ", outie);

                        // Read Firmware version on Myo and display
                        selectedCharacteristic = characs.Characteristics.Single(c => c.Uuid == MYO_FIRMWARE_CH);
                        byte[] readCharac = await read(selectedCharacteristic);
                        UInt16[] _uv = new UInt16[readCharac.Length / 2];
                        System.Buffer.BlockCopy(readCharac, 0, _uv, 0, readCharac.Length);

                        txtFirmware.Text = ($"Firmware Version: {_uv[0]}.{_uv[1]}.{_uv[2]} rev.{_uv[3]}");

                        if (serviceCount > 0)
                        {
                            btnNotifyEMGChars.IsEnabled = true;
                        }
                        else
                        {
                            btnNotifyEMGChars.IsEnabled = false;
                        }
                    }
                }
            }
        }


        private async void Init_EMG_Channels()
        {

            Guid MYO_EMG_SERVICE = new Guid("D5060005-A904-DEB9-4748-2C7F4A124842"); // raw EMG data service
            Guid MYO_EMG_CHAR_0 = new Guid("D5060105-A904-DEB9-4748-2C7F4A124842"); // channel 1
            Guid MYO_EMG_CHAR_1 = new Guid("D5060205-A904-DEB9-4748-2C7F4A124842"); // channel 2
            Guid MYO_EMG_CHAR_2 = new Guid("D5060305-A904-DEB9-4748-2C7F4A124842"); // channel 3
            Guid MYO_EMG_CHAR_3 = new Guid("D5060405-A904-DEB9-4748-2C7F4A124842"); // channel 4


            // Subscribe to EMG Data Characteristics
            GattCharacteristicsResult emgServ = await gatt.Services.Single(s => s.Uuid == MYO_EMG_SERVICE).GetCharacteristicsAsync(BluetoothCacheMode.Uncached);

            GattCharacteristic emg0 = emgServ.Characteristics.Single(c => c.Uuid == MYO_EMG_CHAR_0);
            GattCharacteristic emg1 = emgServ.Characteristics.Single(c => c.Uuid == MYO_EMG_CHAR_1);
            GattCharacteristic emg2 = emgServ.Characteristics.Single(c => c.Uuid == MYO_EMG_CHAR_2);
            GattCharacteristic emg3 = emgServ.Characteristics.Single(c => c.Uuid == MYO_EMG_CHAR_3);

            emg0.ValueChanged += EMG_ValueChanged0;
            emg1.ValueChanged += EMG_ValueChanged1;
            emg2.ValueChanged += EMG_ValueChanged2;
            emg3.ValueChanged += EMG_ValueChanged3;

            GattCommunicationStatus emg0Stat = await enableNotifications(emg0, GattClientCharacteristicConfigurationDescriptorValue.Notify);
            txtEMG0Stat.Text = emg0.CharacteristicProperties + ", " + emg0Stat.ToString();

            GattCommunicationStatus emg1Stat = await enableNotifications(emg1, GattClientCharacteristicConfigurationDescriptorValue.Notify);
            txtEMG1Stat.Text = emg1.CharacteristicProperties + ", " + emg1Stat.ToString();

            GattCommunicationStatus emg2Stat = await enableNotifications(emg2, GattClientCharacteristicConfigurationDescriptorValue.Notify);
            txtEMG2Stat.Text = emg2.CharacteristicProperties + ", " + emg2Stat.ToString();

            GattCommunicationStatus emg3Stat = await enableNotifications(emg3, GattClientCharacteristicConfigurationDescriptorValue.Notify);
            txtEMG3Stat.Text = emg3.CharacteristicProperties + ", " + emg3Stat.ToString();

            if (emg0Stat.ToString() == "Success" && emg1Stat.ToString() == "Success" && emg2Stat.ToString() == "Success" && emg3Stat.ToString() == "Success")
            {
                btnStreamEMG.IsEnabled = true;
                btnStopEMGStream.IsEnabled = true;
            }
            else
            {
                btnStreamEMG.IsEnabled = false;
                btnStopEMGStream.IsEnabled = false;
            }
        }


        private void Disconnect_Myo()
        {

        }





        private async void Stream_EMG_Data()
        {
            // Access CommandCharacteristic
            var characs = await gatt.Services.Single(s => s.Uuid == MYO_SERVICE_GCS).GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
            commandCharacteristic = characs.Characteristics.Single(c => c.Uuid == COMMAND_CHARACT);


            // Write to CommandCharacteristic
            byte[] myoOnData = new byte[] { 0x01, 0x03, 0x02, 0x00, 0x00 };  // { myohw_command_set_mode, payload, myohw_emg_mode_send_emg, myohw_imu_mode_none, myohw_classifier_mode_disabled }   
            emgNotifierConnStatus = await write(commandCharacteristic, myoOnData);
            dispatcherTimer.Start();
        }


        private async void Stop_EMG_Data()
        {
            byte[] stopEMGdata = new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00 };
            var resres = await write(commandCharacteristic, stopEMGdata);
            dispatcherTimer.Stop();
        }


        private byte[] retrieveData (IBuffer characVal)
        {
            DataReader reader = DataReader.FromBuffer(characVal);
            byte[] fileContent = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(fileContent);
            return fileContent;
        }

        int emg1avg = 64;

        List<int> emgCh1 = new List<int>(20);
        List<int> emgCh2 = new List<int>(20);
        List<int> emgCh3 = new List<int>(20);
        List<int> emgCh4 = new List<int>(20);
        List<int> emgCh5 = new List<int>(20);
        List<int> emgCh6 = new List<int>(20);
        List<int> emgCh7 = new List<int>(20);
        List<int> emgCh8 = new List<int>(20);

        private void EMG_ValueChanged0(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            byte[] readBytes = retrieveData(args.CharacteristicValue);
            sbyte[] EMGdataA = new sbyte[8];      
            System.Buffer.BlockCopy(readBytes, 0, EMGdataA, 0, 8);

            // Use the other half of the data to double the resolution (!)
            //sbyte[] EMGdataB = new sbyte[8]; 
            //System.Buffer.BlockCopy(readBytes, 8, EMGdataB, 0, 8);

            emgCh1.Insert(0, Math.Abs((int)EMGdataA[0]));
        }
        private void EMG_ValueChanged1(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            byte[] readBytes = retrieveData(args.CharacteristicValue);
            sbyte[] EMGdataA = new sbyte[8];
            System.Buffer.BlockCopy(readBytes, 0, EMGdataA, 0, 8);
            emgCh1.Insert(0, EMGdataA[0]);  
        }
        private void EMG_ValueChanged2(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            byte[] readBytes = retrieveData(args.CharacteristicValue);
            sbyte[] EMGdataA = new sbyte[8];
            System.Buffer.BlockCopy(readBytes, 0, EMGdataA, 0, 8);
            emgCh1.Insert(0, EMGdataA[0]);
        }
        private void EMG_ValueChanged3(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            byte[] readBytes = retrieveData(args.CharacteristicValue);
            sbyte[] EMGdataA = new sbyte[8];
            System.Buffer.BlockCopy(readBytes, 0, EMGdataA, 0, 8);
            emgCh1.Insert(0, EMGdataA[0]);
        }

        private void Update_Ellipse()
        {
            if (emgCh1.Count > 20)
            {
                emgCh1.RemoveRange(20, emgCh1.Count - 20);

                var vals = emgCh1;
                emg1avg = Math.Abs((vals.Sum() / (vals.Count)) + 25);
                ellEMG0.Height = emg1avg;
                ellEMG0.Width = emg1avg;
            }

        }



        private async void Watcher_Stopped(BluetoothLEAdvertisementWatcher watcher, BluetoothLEAdvertisementWatcherStoppedEventArgs eventArgs)
        {
            byte[] stopEMGdata = new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00 };
            var resres = await write(commandCharacteristic, stopEMGdata);
            Debug.WriteLine(resres);

            Debug.WriteLine($"BLEWATCHER Stopped: {eventArgs}");
        }


        private async void DeviceWatcher_Updated(DeviceWatcher watcher, DeviceInformationUpdate update)
        {
            DeviceInformationCollection di = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelector());
            List<String> keys = update.Properties.Keys.ToList();
            List<Object> vals = update.Properties.Values.ToList();
            List<String> updates = new List<string>();

            for (int x = 0; x < keys.Count(); x++)
            {
                updates.Add($"Update {x}: {keys[x].ToString()}: {vals[x].ToString()}");
                //Debug.WriteLine($"Update {x}: {keys[x].ToString()}: {vals[x].ToString()}");
            }
            await this.Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        txtUpdate.Text = string.Join(Environment.NewLine, updates);
                    });
        }

        private async void DeviceWatcher_Removed(DeviceWatcher watcher, DeviceInformationUpdate args)
        {
            var toRemove = (from a in deviceList where a == args.ToString() select a).FirstOrDefault();
            if (toRemove != null)
            {
                await this.Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Normal,
                    () => { deviceList.Remove(toRemove);
                        txtUpdate.Text += toRemove + " removed from device list" + '\n';
                    });
            }

            deviceList.Remove(toRemove);
            //Debug.WriteLine($"{args.ToString()} removed from device list");      
        }

        private async void DeviceWatcher_Added(DeviceWatcher watcher, DeviceInformation args)
        {

            await this.Dispatcher.RunAsync(
                Windows.UI.Core.CoreDispatcherPriority.Normal,
                () => { deviceList.Add(args.Name); });

            Debug.WriteLine($"Device Added: {args.Name}");
            List<String> keys = args.Properties.Keys.ToList();
            List<Object> vals = args.Properties.Values.ToList();

            var dictionary = keys.Zip(vals, (k, v) => new { Key = k, Value = v })
                     .ToDictionary(x => x.Key, x => x.Value);
            var devAddr = (from d in dictionary
                          where d.Key.Contains("DeviceAddress")
                          select d.Value).FirstOrDefault();
            foreach (string key in dictionary.Keys)
            {
                Debug.WriteLine(key);
            }
        }




        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            Debug.WriteLine("stopping watchers..");
            BleWatcher.Stop();
            deviceWatcher.Stop();
            
            deviceWatcher.Added -= DeviceWatcher_Added;
            deviceWatcher.Updated -= DeviceWatcher_Updated;
            deviceWatcher.Removed -= DeviceWatcher_Removed;

            BleWatcher.Received -= WatcherOnReceived;
            BleWatcher.Stopped -= Watcher_Stopped;

            base.OnNavigatedFrom(e);
        }





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
    }
}



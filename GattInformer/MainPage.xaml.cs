using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;

using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

using GattChar = Windows.Devices.Bluetooth.GenericAttributeProfile.GattCharacteristic;
using Windows.Security.Cryptography;

namespace GattInformer
{
    public partial class MainPage
    {
        #region Variables

        private BluetoothLEAdvertisementWatcher BleWatcher;

        private string deviceFilterString;
        private string deviceName;
        private string deviceConnectionStatus;
        private string devBluetoothAddress;
        private string _txtUpdate;
        private string _noServices;

        // Vars for incoming data handling
        private int noRcvd = 0;
        private int droppedPackets = 0;
        private string finalData = "";
        private byte[] midData = new byte[0];

        // bluetooth address book
        private Dictionary<ulong, string> addressBook = new Dictionary<ulong, string>();

        // BT active objects
        private BluetoothLEDevice currentDevice;
        public ObservableCollection<Button> DeviceButtons = new ObservableCollection<Button>();
        private List<GattDeviceService> myServices = new List<GattDeviceService>();
        private List<GattCharacteristic> myCharacs = new List<GattCharacteristic>();
        private GattCharacteristic activeCharac;
        
        private bool isPairing = false;

        private GattDeviceServicesResult gatt = null;
        private GattDeviceService targetService;
        private GattCharacteristic IMUdataChar;
        float[] currentIMUData;

        private const GattClientCharacteristicConfigurationDescriptorValue _notify =
            GattClientCharacteristicConfigurationDescriptorValue.Notify;

        private const GattClientCharacteristicConfigurationDescriptorValue _indicate =
            GattClientCharacteristicConfigurationDescriptorValue.Indicate;

        private const GattClientCharacteristicConfigurationDescriptorValue _unnotify =
            GattClientCharacteristicConfigurationDescriptorValue.None;

        private DispatcherTimer dispatcherTimer = new DispatcherTimer();
        private double captureDuration = 0;

        #endregion Variables


        #region XAML UI Bindings

        // Bindings for XAML buttons
        private void btnFilterDevices_Click(object sender, RoutedEventArgs e)
        {
            ApplyFilter();
        }

        private void txtToFind_GotFocus(object sender, RoutedEventArgs e)
        {
            txtToFind.SelectAll();
        }

        private void txtToFind_TextChanged(object sender, RoutedEventArgs e)
        {
            ApplyFilter();
        }

        private void txtToWrite_GotFocus(object sender, RoutedEventArgs e)
        {
            txtToWrite.SelectAll();
        }

        private void GetServices_Click(object sender, RoutedEventArgs e)
        {
            GetServices(currentDevice);
        }

        private void PairDevice_Click(object sender, RoutedEventArgs e)
        {
            PairWithDevice(currentDevice);
        }

        private void btnRefresh_Click(object sender, RoutedEventArgs e)
        {
            Refresh();
        }

        private void btnClearUpdate_Click(object sender, RoutedEventArgs e)
        {
            txtUpdate.Text = "";
        }

        #endregion XAML UI Bindings


        public MainPage()
        {
            this.InitializeComponent();
            
            deviceFilterString = "";

            // uncomment to allow timer
            dispatcherTimer.Tick += dispatcherTimer_Tick;
            dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, 100);

            // Create and start watcher
            SetupAdvertWatcher();
            BleWatcher.Start();

            icDevices.ItemsSource = DeviceButtons;

            UiUpdate();
        }



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
                    if (currentIMUData != null)
                    {
                        txtNotify.Text = ($"W:{currentIMUData[0]} \n X:{currentIMUData[1]} \n Y:{currentIMUData[2]} \n Z:{currentIMUData[3]}");
                    }
                });
        }

        #endregion Timer


        private void ApplyFilter()
        {
            deviceFilterString = txtToFind.Text;
            UpdateDeviceButtons(deviceFilterString);
            UiUpdate();
        }


        public Task<bool> UiUpdate()
        {
            var tcs = new TaskCompletionSource<bool>();
            Task.Run(async () =>
            {
                await this.Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        txtToFind.Text = deviceFilterString ?? "";
                        txtDevice.Text = deviceName ?? "";
                        txtBTAddr.Text = devBluetoothAddress ?? "";
                        txtDevConnStat.Text = deviceConnectionStatus ?? "";
                        txtServCnt.Text = _noServices ?? "";
                        txtUpdate.Text = _txtUpdate ?? "";

                        if (gatt != null)
                        {
                            if (gatt.Status == GattCommunicationStatus.Success)
                            {
                                btnGetServices.IsEnabled = true;
                                btnPair.IsEnabled = true;
                            }
                            else
                            {
                                txtDevConnStat.Text = gatt.Status.ToString();
                                btnGetServices.IsEnabled = false;
                                btnPair.IsEnabled = false;
                            }
                        }
                    });
            });
            return tcs.Task;
        }


        #region Setup Watchers, Handle Advert Data

        private void SetupAdvertWatcher()
        {
            // BLE Advert Watcher
            BleWatcher = new BluetoothLEAdvertisementWatcher();
            BleWatcher.ScanningMode = BluetoothLEScanningMode.Active;
            BleWatcher.SignalStrengthFilter.InRangeThresholdInDBm = -80;
            BleWatcher.SignalStrengthFilter.OutOfRangeTimeout = TimeSpan.FromSeconds(1);

            BleWatcher.Received += WatcherOnReceived;
            BleWatcher.Stopped += Watcher_Stopped;
        }


        
        private void HandleWatcher(BluetoothLEAdvertisementReceivedEventArgs watcherArgs)
        {
            string _name = watcherArgs.Advertisement.LocalName;
            ulong btAddr = watcherArgs.BluetoothAddress;
            string addrStr = btAddr.ToString();

            if (_name.Length == 0)
            {
                _name = "? " + addrStr;
            }

            if (!addressBook.ContainsKey(btAddr))
            {
                addressBook.Add(btAddr, _name);
                if (_name.Trim().ToLower().Contains(deviceFilterString.Trim().ToLower()))
                {
                    CreateDeviceButton(addressBook.Last());
                }
            }
            
            try
            {
                Task.Run(async () => {
                    BluetoothLEDevice attemptDev = await getBLEDeviceFromAddress(watcherArgs.BluetoothAddress);
                    if (attemptDev != null)
                    {
                        UpdateAddressBookEntry(watcherArgs.BluetoothAddress, attemptDev.Name);
                    }
                });
            }
            catch
            {
                //_txtUpdate += Environment.NewLine + "Device connection attempt failed"; 
                Debug.WriteLine("device connection attempt failed...");
            }

            /* snooping on data sections
            List<byte[]> dataSections = new List<byte[]>();
            foreach (BluetoothLEAdvertisementDataSection ds in watcherArgs.Advertisement.DataSections)
            {
                dataSections.Add(readFromBuffer(ds.Data));
                Debug.WriteLine("<" + btAddr + ">\t" + ByteArrayToString(dataSections.Last()));
            }
            */

            //UiUpdate();
        }

        private async void CreateDeviceButton(KeyValuePair<ulong, string> kvp)
        {
            string storedDeviceName = "";
            if (addressBook.ContainsKey(kvp.Key) && addressBook[kvp.Key] != kvp.Value)
            {
                storedDeviceName = addressBook[kvp.Key];
            }

            Console.WriteLine("starting button creation");

            await this.Dispatcher.RunAsync(

                Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    Button newBtn = new Button();
                    if (storedDeviceName != "")
                    {
                        newBtn.Content = storedDeviceName;
                    }
                    else
                    {
                        newBtn.Content = kvp.Value;
                    }

                    newBtn.Name = kvp.Key.ToString();
                    newBtn.Tag = kvp.Value;

                    newBtn.Padding = new Thickness(6);
                    newBtn.IsTextScaleFactorEnabled = true;
                    newBtn.FontSize = 12;
                    newBtn.BorderBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.WhiteSmoke);
                    newBtn.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.DarkSlateGray);
                    newBtn.Click += DeviceButtonClick;

                    DeviceButtons.Add(newBtn);
                    icDevices.Height = (DeviceButtons.Count * 30);
                });
        }

        private void UpdateDeviceButtons(string filteredBy)
        {
            DeviceButtons.Clear();
            GC.Collect();

            foreach(KeyValuePair<ulong, string> kvp in addressBook)
            {
                string tempName = kvp.Value.Trim().ToLower();
                    
                if (tempName.Contains(filteredBy.Trim().ToLower()))
                {
                    CreateDeviceButton(kvp);
                }
            }
            icDevices.Height = (DeviceButtons.Count * 30);
        }


        private async Task<BluetoothLEDevice> getBLEDeviceFromAddress(ulong btAddress)
        {
            return await BluetoothLEDevice.FromBluetoothAddressAsync(btAddress);
        }

        private async void DeviceButtonClick(object sender, RoutedEventArgs e)
        {
            spServices.Children.Clear();
            spServices.Height = 0;
            spCharacs.Children.Clear();
            spCharacs.Height = 0;
            _txtUpdate = "";

            ulong deviceAddress = UInt64.Parse((sender as Button).Name as string);

            
            currentDevice = await getBLEDeviceFromAddress(deviceAddress);
            
            await ProbeDevice(currentDevice);
        }




        #endregion Setup Watchers, Handle Advert Data


        #region Device Handling

        private async void DisplayDeviceInfo(BluetoothLEDevice dev)
        {
            await this.Dispatcher.RunAsync(
                Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    //txtDevice.Text = dev.Name.ToString();
                    devBluetoothAddress = dev.BluetoothAddress.ToString();
                    deviceName = dev.Name.ToString();

                    UpdateAddressBookEntry(dev.BluetoothAddress, dev.Name);

                    deviceConnectionStatus = dev.ConnectionStatus.ToString();  // 'sometimes stays "connected"
                    _noServices = gatt.Services.Count.ToString();

                });

            await UiUpdate();
        }

        
        private async void PairWithDevice(BluetoothLEDevice _dev)
        {
            if (isPairing) return;

            isPairing = true;
            DevicePairingResult result = await _dev.DeviceInformation.Pairing.PairAsync(DevicePairingProtectionLevel.None);

            if (result.Status == DevicePairingResultStatus.Paired || result.Status == DevicePairingResultStatus.AlreadyPaired){
                _txtUpdate += Environment.NewLine + "Pairing successful";
            }
            else if (result.Status == DevicePairingResultStatus.AlreadyPaired)
            {
                _txtUpdate += Environment.NewLine + _dev.Name + " is already paired";
            }
            else
            {
                _txtUpdate += Environment.NewLine + "Pairing failed";
            }
            txtDevConnStat.Text = result.Status.ToString();
            isPairing = false;
        }

        #endregion Device Handling

        private async Task<int> ProbeDevice(BluetoothLEDevice subject)
        {
            try
            {
                if (subject != null)
                {
                    gatt = await subject.GetGattServicesAsync(BluetoothCacheMode.Uncached);
                    _txtUpdate += Environment.NewLine + ("Probing device: " + subject.Name);
                }
            }
            catch
            {
                _txtUpdate += Environment.NewLine + "[PD.Err1] Gatt not responsive, might be busy or switched off";
                return 1;
            }

            if (gatt == null)
            {
                _txtUpdate += Environment.NewLine + "[PD.Err2] Gatt was null, please try again";
                return 2;
            }

            DisplayDeviceInfo(subject);
            return 0;
        }



        #region Service & Characteristic Handling

        private async void GetServices(BluetoothLEDevice _device)
        {

            int errCheck = await ProbeDevice(_device);
            if (errCheck < 0) { return; }

            myServices.Clear();
            spServices.Children.Clear();
            spServices.Height = 0;

            spCharacs.Children.Clear();
            spCharacs.Height = 0;

            foreach (GattDeviceService gds in gatt.Services)
            {
                myServices.Add(gds);

                Button newBtn = new Button();

                newBtn.Content = gds.AttributeHandle + ": " + gds.Uuid;
                newBtn.Name = gds.Uuid.ToString();
                newBtn.Tag = gds.AttributeHandle.ToString();

                newBtn.Padding = new Thickness(15);
                newBtn.IsTextScaleFactorEnabled = true;
                newBtn.FontSize = 11;
                newBtn.BorderBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Yellow);
                newBtn.BorderThickness = new Thickness(1);
                newBtn.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.MidnightBlue);

                newBtn.Click += ServiceButtonClicked;

                spServices.Height += 48;
                spServices.Children.Add(newBtn);

            }

        }

        private void ServiceButtonClicked(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine(string.Format("You clicked on service {0}.", (sender as Button).Tag));

            string serviceGuidstr = (sender as Button).Name.ToString();
            InvestigateService(new Guid(serviceGuidstr));
            
        }

        private async void InvestigateService(Guid serviceGuid)
        {
            if (myServices.Count > 0)
            {
                targetService = myServices.Single(s => s.Uuid == serviceGuid);
                // To do: highlight button, unhighlight previous one
            }

            if (gatt.Status == GattCommunicationStatus.Success)
            {
                try
                {
                    GattCharacteristicsResult gattCharacRes = await targetService.GetCharacteristicsAsync();
                    myCharacs = gattCharacRes.Characteristics.ToList();
                }
                catch
                {
                    Debug.WriteLine("Error caught - probably a legacy device or out of bounds");
                    await UiUpdate();
                    return;
                }

                // update chracteristic list
                spCharacs.Children.Clear();
                spCharacs.Height = 0;

                foreach (GattCharacteristic ch in myCharacs)
                {
                    Button newBtn = new Button();
                    
                    newBtn.Name = ch.Uuid.ToString();
                    newBtn.Tag = ch.AttributeHandle.ToString();

                    newBtn.Content = "Charac." + ch.AttributeHandle + ": " + ch.Uuid + "\n" + ch.CharacteristicProperties + "\n";
                    newBtn.Padding = new Thickness(10);
                    newBtn.IsTextScaleFactorEnabled = true;
                    newBtn.FontSize = 10;
                    newBtn.BorderBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Teal);
                    newBtn.BorderThickness = new Thickness(1);
                    newBtn.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.MidnightBlue);
                    newBtn.Click += CharacButtonClicked;

                    

                    if (ch.CharacteristicProperties.ToString().Contains("Read"))
                    {
                        string characValue = await TryReadingCharac(ch);
                        newBtn.Content += $"Value:{characValue}";
                    }


                    spCharacs.Height += 70;
                    spCharacs.Children.Add(newBtn);
                }

                //update connection status
                deviceConnectionStatus = currentDevice.ConnectionStatus.ToString();
            }

            await UiUpdate();
        }

        private async void CharacButtonClicked(object sender, RoutedEventArgs e)
        {
            Debug.Write(string.Format("You clicked on characteristic {0}.", (sender as Button).Tag));
            

            Button clickedBtn = sender as Button;
            activeCharac = myCharacs.Single(c => c.Uuid == new Guid(clickedBtn.Name));
            Debug.WriteLine(string.Format(" Flags: {0}.", activeCharac.CharacteristicProperties.ToString()));




            string characData = await TryReadingCharac(activeCharac);
            if (characData != "")
            {
                clickedBtn.Content += " " + characData + ", ";
            }

            //byte[] writeBytes = new byte[] { 0x01, 0x02, 0x03 }; //AT+FSM=FSM_TRANS_USB_COM_BLE
            //var response = await write(activeCharac, bytes);
            //tryWritingCharac(activeCharac, bytes);

            await tryNotifyingCharac(activeCharac, _notify);
            //clickedBtn.Content += activeCharac.UserDescription;

            
        }


        private async void OnDeviceConnectionUpdated(bool isConnected)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (isConnected)
                {
                    txtUpdate.Text = "Connected!";
                }
                else
                {
                    txtUpdate.Text = "Waiting for device to connect...";
                }
            });
        }







        #endregion Service & Characteristic Handling


        #region Data Handling


        private async Task<string> TryReadingCharac(GattCharacteristic ch)
        {
            try
            {
                byte[] readData = await read(ch);
                string s = Encoding.UTF8.GetString(readData, 0, readData.Length);
                Debug.WriteLine($"Charac {ch.AttributeHandle} has value '{s}' ({readData.Length})");
                return s;
            }
            catch
            {
                Debug.WriteLine($"Error reading characteristic {ch.AttributeHandle}");
                return "";
            }
        }




        private async void tryWritingCharac(GattCharacteristic ch, byte[] bytesToWrite)
        {
            try
            { 
                var response = await write(ch, bytesToWrite);
                Debug.WriteLine("Writing to charac. was a success");
            }
            catch
            {
                Debug.WriteLine($"Error writing to characteristic {ch.AttributeHandle}");
            }
        }




        private async Task tryNotifyingCharac(GattCharacteristic ch, GattClientCharacteristicConfigurationDescriptorValue val)
        {    
            try
            {
                var notificationTask = await notify(ch, val);

                if (notificationTask == GattCommunicationStatus.Success)
                {
                    //ch.ValueChanged += MysteryCharacValue;
                    Debug.WriteLine("notification was a success");
                    dispatcherTimer.Start();
                }

            }
            catch
            {
                Debug.WriteLine($"Error notifying characteristic {ch.AttributeHandle}");
                dispatcherTimer.Stop();
            }
        }

        private void MysteryCharacValue(GattChar sender, GattValueChangedEventArgs args)
        {
            
            string dataFromNotify = "";
            Task.Run(() => {
                CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out byte[] foundValue);
                //var sfoundValue = await sender.ReadValueAsync();
                //byte[] foundValue = readFromBuffer(sfoundValue.Value);
                dataFromNotify = Encoding.ASCII.GetString(foundValue);
            });
            Debug.WriteLine("Mystery Charac Data: " + dataFromNotify);
        }

        private void GotIMUData(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            Debug.WriteLine("Got some data: " + args.CharacteristicValue.ToString());

            byte[] newData = readFromBuffer(args.CharacteristicValue);
            currentIMUData = new float[newData.Length];
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




        private void WatcherOnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            ulong _addr = args.BluetoothAddress;
            HandleWatcher(args);

        }

        private async void UpdateAddressBookEntry(ulong bleAddr, string devName)
        {
            if (addressBook.Keys.Contains(bleAddr) && addressBook[bleAddr] != devName)
            {
                _txtUpdate += Environment.NewLine + ("Discrepancy found: " + devName + ", updating address book");
                //Debug.WriteLine("Discrepancy found: " + devName + ", updating address book");

                await this.Dispatcher.RunAsync(
                Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    addressBook[bleAddr] = devName;
                    UpdateDeviceButtons(deviceFilterString);
                });
            }
        }


        private void Watcher_Stopped(BluetoothLEAdvertisementWatcher watcher, BluetoothLEAdvertisementWatcherStoppedEventArgs eventArgs)
        {
            _txtUpdate = "Watcher fault - please check that bluetooth is switched on";
            Debug.WriteLine($"Watcher fault - please check that bluetooth is switched on.");
        }





        #region Read, Write, Notify and Indicate

        public async Task<byte[]> read(GattCharacteristic c)
        {
            if (c.CharacteristicProperties.ToString().Contains("Read"))
            {
                byte[] response = (await c.ReadValueAsync()).Value.ToArray();
                return response;
            }
            else
            {
                Debug.WriteLine("Read attempt failed... Not a 'read' characteristic");
                return new byte[] { 0x99 };
            }
        }
        
        public async Task<GattCommunicationStatus> write(GattCharacteristic c, byte[] data)
        {
            if (c.CharacteristicProperties.ToString().Contains("Write"))
            {
                GattWriteResult writeResult = await c.WriteValueWithResultAsync(data.AsBuffer(), GattWriteOption.WriteWithResponse);
                Debug.WriteLine("Write result:" + writeResult.ToString());
                return writeResult.Status;
            }
            else if (c.CharacteristicProperties.ToString().Contains("WriteWithoutResponse"))
            {
                return await c.WriteValueAsync(data.AsBuffer(), GattWriteOption.WriteWithoutResponse);
            }
            else
            {
                Debug.WriteLine("Write attempt failed... Not a 'write' characteristic");
                return GattCommunicationStatus.ProtocolError;
            }
            
        }

        public async Task<GattCommunicationStatus> notify(GattCharacteristic characteristic, GattClientCharacteristicConfigurationDescriptorValue value)
        {

            GattCommunicationStatus notifyResult;
            if (characteristic.CharacteristicProperties.ToString().Contains("Notify"))
            {
                notifyResult = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                characteristic.ValueChanged += Charac_ValueChangedAsync;
            }
            else
            {
                Debug.WriteLine("Notify attempt failed... Not a 'notify' characteristic");
                notifyResult = GattCommunicationStatus.ProtocolError;
                dispatcherTimer.Stop();
            }

            return notifyResult;
        }

        public async Task<GattCommunicationStatus> indicate(GattCharacteristic c, byte[] data)
        {
            if (c.CharacteristicProperties.ToString().Contains("Indicate"))
            {
                return await c.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Indicate);
            }
            else
            {
                Debug.WriteLine("Indicate attempt failed... Not an 'indicate' characteristic");
                return GattCommunicationStatus.ProtocolError;
            }

        }

        

        

        private float[] AdaFloatArrayFromBytes(byte[] rawBytes, int bytesLength)
        {
            if (rawBytes.Length > bytesLength)
            {
                droppedPackets++;
                Debug.WriteLine("too many bytes: " + rawBytes.Length);
                return null;
            }
            else if (rawBytes.Length < bytesLength)
            {
                if (midData.Length == 0)
                {
                    midData = new byte[rawBytes.Length];
                    midData = rawBytes;
                    return null;
                }
                else
                {
                    byte[] newB = new byte[midData.Length + rawBytes.Length];
                    System.Buffer.BlockCopy(midData, 0, newB, 0, midData.Length);
                    System.Buffer.BlockCopy(rawBytes, 0, newB, midData.Length, rawBytes.Length);

                    if (newB.Length > 8) // gone too far, drop it
                    {
                        droppedPackets++;
                        Debug.WriteLine(droppedPackets + " arrays overload (" + newB.Length + ")");
                        midData = new byte[0];
                        return null;
                    }
                    else if (newB.Length < 8) // not full yet, keep concatenating
                    {
                        midData = new byte[newB.Length];
                        midData = newB;
                        return null;
                    }
                    else // hit target size, use data
                    {
                        rawBytes = newB;
                    }
                }
            }

            float[] fltArray = new float[bytesLength / 2];

            if (rawBytes.Length == 8)
            {
                short quatW = BitConverter.ToInt16(rawBytes, 0);
                short quatX = BitConverter.ToInt16(rawBytes, 2);
                short quatY = BitConverter.ToInt16(rawBytes, 4);
                short quatZ = BitConverter.ToInt16(rawBytes, 6);
                fltArray = new float[4] { (float)quatW / 32767.0f, (float)quatX / 32767.0f, (float)quatY / 32767.0f, (float)quatZ / 32767.0f };
            }

            return fltArray;

        }

        
        private string CharacStringFromBytes(byte[] rawBytes, int stringLength)
        {
            string dataFromNotify;
            dataFromNotify = Encoding.ASCII.GetString(rawBytes);
            byte [] data = Encoding.UTF8.GetBytes(dataFromNotify);

            if (dataFromNotify.Length == stringLength)
            {
                return dataFromNotify;
            }
            else
            {
                finalData = finalData + dataFromNotify;

                if (finalData.Length == stringLength)
                {
                    finalData = "";
                    return finalData;
                }
                else
                {
                    droppedPackets++;
                    Debug.WriteLine(droppedPackets + " arrays overload (" + finalData.Length + ")");
                    return finalData;
                }
            }
            
        }


        private void Charac_ValueChangedAsync(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            noRcvd++;
            CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out byte[] data);

            float[] newQuat = AdaFloatArrayFromBytes(data, 8);

            if (newQuat != null)
            {
                //Debug.WriteLine($"{noRcvd} quaternions received: {newQuat[0]},{newQuat[1]},{newQuat[2]},{newQuat[3]}");
                currentIMUData = newQuat;
            }


            
        }


        #endregion Read, Write and Notify


        private void Refresh()
        {
            DeviceButtons.Clear();

            spServices.Children.Clear();
            spServices.Height = 0;
            spCharacs.Children.Clear();
            spCharacs.Height = 0;
 
            btnGetServices.IsEnabled = false;
            btnPair.IsEnabled = false;

            myServices.Clear();
            myCharacs.Clear();

            if (IMUdataChar != null)
            {
                IMUdataChar = null;
            }
            //IMUdataChar = IMUdataChar ?? null; // > C# 8

            if (targetService != null)
            {
                targetService.Dispose();
                targetService = null;
            }

            if (gatt != null)
            {
                gatt = null;
            }

            if (currentDevice != null)
            {
                currentDevice.Dispose();
                currentDevice = null;
            }

            GC.Collect();

            deviceFilterString = "";
            deviceName = "";
            devBluetoothAddress = "";
            deviceConnectionStatus = "";
            _noServices = "";

            UpdateDeviceButtons("");
            UiUpdate();
            
        }

        public void CloseApplication()
        {
            dispatcherTimer.Stop();

            BleWatcher.Received -= WatcherOnReceived;
            BleWatcher.Stopped -= Watcher_Stopped;

            Refresh();

            Debug.WriteLine("stopping watchers...");
            BleWatcher.Stop();
        }

    }
}



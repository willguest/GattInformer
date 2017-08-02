﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;

using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

namespace BluAdvert
{
    public class BluetoothImplementation
    {
        // Define BLE current device getters and setters.  
        public BluetoothLEDevice currentDevice { get; set; }
        // Define Devices List.  
        public List<string> deviceList = new List<string>();
        // Define Service List.  
        public List<string> serviceList = new List<string>();
        // Define Characteristic List.  
        public List<string> characteristicList = new List<string>();
        // Define the selectedCharacteristic variable.  
        private GattCharacteristic selectedCharacteristic;
        // Define the selectedService variable.  
        private GattDeviceService selectedService;
        /// <summary>  
        /// Characteristic index.  
        /// </summary>  
        private const int CHARACTERISTIC_INDEX = 0;
        /// <summary>  
        /// Characteristic notification type Notify enable.  
        /// </summary>  
        private const GattClientCharacteristicConfigurationDescriptorValue CHARACTERISTIC_NOTIFICATION_TYPE = GattClientCharacteristicConfigurationDescriptorValue.Notify;

        public BluetoothImplementation()
        {
            Debug.WriteLine("beginning");


        }


        /// <summary>  
        /// Get bluetooth devices.  
        /// </summary>  
        /// <returns>deviceList</returns>  
        public async Task<List<string>> getDevices()
        {
            foreach (DeviceInformation di in await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelector()))
            {
                BluetoothLEDevice bleDevice = await BluetoothLEDevice.FromIdAsync(di.Id);
                // Add the dvice name into the list.  
                deviceList.Add(bleDevice.Name);
            }
            return deviceList;
        }
        /// <summary>  
        /// Connect to the desired device.  
        /// </summary>  
        /// <param name="deviceName"></param>  
        public async void selectDevice(string deviceName)
        {
            foreach (DeviceInformation di in await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelector()))
            {
                BluetoothLEDevice bleDevice = await BluetoothLEDevice.FromIdAsync(di.Id);
                // Check if the name of the device founded is EMU Bridge.  
                if (bleDevice.Name == deviceName)
                {
                    // Save detected device in the current device variable.  
                    currentDevice = bleDevice;
                    break;
                }
            }
        }




        /// <summary>  
        /// Read value from characteristic. Reverse the result using Array.Reverse.  
        /// </summary>  
        /// <returns>read response</returns>  
        public async Task<byte[]> read()
        {
            byte[] response = (await selectedCharacteristic.ReadValueAsync()).Value.ToArray();
            Array.Reverse(response, 0, response.Length);
            return response;
        }


        /// <summary>  
        /// Write a byte[] into characteristic.  
        /// </summary>  
        /// <param name="characteristic"></param>  
        /// <param name="data"></param>  
        /// <returns>communication status</returns>  
        public async Task<GattCommunicationStatus> write(GattCharacteristic characteristic, byte[] data)
        {
            return await characteristic.WriteValueAsync(data.AsBuffer(), GattWriteOption.WriteWithResponse);
        }


        /// <summary>  
        /// Enable notifications from the specified characteristic.  
        /// </summary>  
        /// <param name="characteristic"></param>  
        /// <returns>communication status</returns>  
        public async Task<GattCommunicationStatus> enableNotifications(GattCharacteristic characteristic)
        {
            GattCommunicationStatus status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(CHARACTERISTIC_NOTIFICATION_TYPE);
            if (status == GattCommunicationStatus.Unreachable)
            {
                Debug.WriteLine("Your device is unreachable!");
            }
            else
            {
                Debug.WriteLine("Service initializated");
            }
            return status;
        }

        public void CloseApp()
        {
            Debug.Write("stopping");
            Windows.ApplicationModel.Core.CoreApplication.Exit();
        }

    }
}
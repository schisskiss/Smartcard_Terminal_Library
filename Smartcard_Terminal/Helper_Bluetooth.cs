﻿using System;
using System.Threading;
using System.IO;
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;
using System.Collections.Generic;

/// <summary>
/// Class for the Bluetooth-Connection
/// Accept connections and send and receive Data
/// </summary>
namespace Smartcard_Terminal
{
    /// <summary>
    /// Static Calss for static Objects
    /// </summary>
    public static class Messages_List
    {
        public static List<collection_Messages> _Messages = new List<collection_Messages>();
        public static int messageNumber = 0;
    }

    /// <summary>
    /// Bluetooth-Class
    /// </summary>
    public class Helper_Bluetooth
    {
        Smartcard_Terminal scTerminal;
        CryptLib cryptLib;
        Thread receiveNewMessages;
        AcceptMessages acceptMessages;

        private static System.Timers.Timer aTimer;
        private receiveMessageCallback newMessageCallback;

        private BluetoothRadio myRadio;
        public BluetoothListener btListener { get; set; }
        public static BluetoothClient client { get; set; }

        private string btAddress;
        private string btName;

        private string AES_KEY;
        private string AES_IV;

        private Guid MyServiceUuid = new Guid("{00001101-0000-1000-8000-00805F9B34FB}"); 

        private static string connectionState { get; set; }
        private static Boolean is_BT_Connected { get; set; }

        /// <summary>
        /// Class Constructor
        /// </summary>
        /// <param name="scTerminal">Smartcard_Terminal instance</param>
        /// <param name="callbackDelegate">receiveMessageCallback object</param>
        public Helper_Bluetooth(Smartcard_Terminal scTerminal, receiveMessageCallback callbackDelegate)
        {
            this.scTerminal = scTerminal;
            newMessageCallback = callbackDelegate;
            AES_KEY = string.Empty;
            AES_IV = string.Empty;
        }

        /// <summary>
        /// Public Method for checking if Bluetooth Device is accessible
        /// </summary>
        /// <returns>null if no Device and the Device Address if one is available</returns>
        public string Get_BT_Device()
        {
            try
            {
                client = new BluetoothClient();
                myRadio = BluetoothRadio.PrimaryRadio;

                if (myRadio == null)
                {
                    return null;
                }
                
                RadioMode mode = myRadio.Mode;
                // Warning: LocalAddress is null if the radio is powered-off.
                BluetoothAddress addr = myRadio.LocalAddress;
                btAddress = addr.ToString("C");

                //myRadio.Mode = RadioMode.Discoverable;
                myRadio.Mode = RadioMode.Connectable;

                return btAddress;
            }
            catch (Exception e)
            {
                return null;
            }
        }


        /// <summary>
        /// Public Method for returning the Bluetooth Name of Connected Device
        /// </summary>
        /// <returns>Device Name if Device is Connected, else null</returns>
        public string Get_BT_Name()
        {
            if (client != null)
            {
                if (client.Connected)
                {
                    return btName;
                }
            }
            return null;
        }

        /// <summary>
        /// Public Method that starts BluetoothListener
        /// </summary>
        /// <returns>Returns true if Thread Start Successfull</returns>
        public Boolean StartBTListener()
        {
            try
            {
                btListener = new BluetoothListener(MyServiceUuid);
                btListener.Authenticate = false;
                btListener.Start(1);
                return true;
            } catch (IOException e)
            {
                return false;
            }   
        }

        /// <summary>
        /// Method for Accepting connection request
        /// </summary>
        /// <returns>true is connection is estabished</returns>
        public Boolean openConnection()
        {
            Console.WriteLine("Accept Connection...");

            try
            {
                client = btListener.AcceptBluetoothClient();
            }
            catch (Exception e)
            {
                Console.WriteLine("Connection Refused.");
                return false;
            }

            if (client.Connected)
            {
                btName = client.RemoteMachineName;
                Console.WriteLine("Connection established.");
            }

            Stream peerStream = client.GetStream();
            StreamReader wtr_2 = new StreamReader(peerStream);

            String msg = wtr_2.ReadLine();

            String[] stringSeparators = new String[] { ">>" };
            String[] message = msg.Split(stringSeparators, StringSplitOptions.None);

            int code = Int32.Parse(message[1]);
            string data = message[2];

            if (code == 0 && data.Equals("connected"))
            {
                if (message.Length == 4)
                {
                    if (scTerminal.Get_encryption_state())
                    {
                        scTerminal.Set_public_B(message[3]);
                        cryptLib = new CryptLib();

                        AES_KEY = CryptLib.getHashSha256(scTerminal.Get_Shared_Secret(), 32);
                        AES_IV = CryptLib.getHashSha256(scTerminal.Get_Shared_Secret(), 16);
                    }
                }

                SendMessage(1, "connected");

                is_BT_Connected = true;
                btName = client.RemoteMachineName;

                acceptMessages = new AcceptMessages(client, new receiveCallback(ResultCallback));
                receiveNewMessages = new Thread(new ThreadStart(acceptMessages.CheckForMessages));
                receiveNewMessages.Start();

                return true;
            }
            else
            {
                SendMessage(1, "refused");
                collection_Messages tmp = new collection_Messages(0, 2, "refused");
                newMessageCallback(tmp);
                is_BT_Connected = false;
            }
            return false;
        }

        /// <summary>
        /// Method for sending async message
        /// </summary>
        /// <param name="code">Code for Android-App</param>
        /// <param name="msg">Message</param>
        /// <returns>true if message is send</returns>
        public Boolean SendMessage(int code, String msg)
        {
            if (client.Connected)
            {
                Stream peerStream = client.GetStream();
                StreamWriter wtr_1 = new StreamWriter(peerStream);

                try
                {
                    if (scTerminal.Get_encryption_state())
                    {
                        msg = cryptLib.encrypt(msg, AES_KEY, AES_IV);
                    }

                    wtr_1.WriteLine("0>>" + code + ">>" + msg);
                    wtr_1.Flush();
                }
                catch (IOException e)
                {
                    return false;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Method for sync message send
        /// </summary>
        /// <param name="code">Code for Android-App</param>
        /// <param name="msg">Message</param>
        /// <returns>returns received message</returns>
        public collection_Messages SendandReceiveMessage(int code, String msg)
        {
            if (client.Connected)
            {
                Stream peerStream = client.GetStream();
                StreamWriter wtr_1 = new StreamWriter(peerStream);
                
                Messages_List.messageNumber++;

                if (scTerminal.Get_encryption_state())
                {
                    msg = cryptLib.encrypt(msg, AES_KEY, AES_IV);
                }

                String sendString = Messages_List.messageNumber + ">>" + code + ">>" + msg;
                int _ID = Messages_List.messageNumber;

                try
                {
                    wtr_1.WriteLine(sendString);
                    wtr_1.Flush();
                } catch(IOException e)
                {
                    return null;
                }

                int icount = 0;
                while(icount < 1000)
                {
                    for (int i = 0; i < Messages_List._Messages.Count; i++)
                    {
                        collection_Messages tmp = Messages_List._Messages[i];
                        if (tmp.ID.Equals(_ID))
                        {
                            if (scTerminal.Get_encryption_state())
                            {
                                tmp.Message = cryptLib.decrypt(tmp.Message, AES_KEY, AES_IV);
                            }

                            Messages_List._Messages.RemoveAt(i);
                            return tmp;
                        }
                    }

                    icount++;
                    Thread.Sleep(10);
                }
            }
            else
            {
                ResultCallback(0, 99, "Connection_Lost");
                StopBTConnection();
            }
            return null;
        }

        /// <summary>
        /// Method for cancel Bluetooth connection
        /// </summary>
        public void StopBTConnection()
        {
            if (client.Connected)
            {
                SendMessage(1, "application_stop");
                try
                {
                    client.Close();
                }
                catch (Exception e)
                {
                    //Console.WriteLine("Error: " + e);
                }
            }

            if (btListener != null)
            {
                try
                {
                    btListener.Stop();
                }
                catch (Exception e)
                {
                    //Console.WriteLine("Error: " + e);
                }
            }

            if (receiveNewMessages != null)
            {
                if (receiveNewMessages.IsAlive)
                {
                    try
                    {
                        receiveNewMessages.Abort();
                    }
                    catch (Exception e)
                    {
                        //Console.WriteLine("Error: " + e);
                    } 
                }
            }
        }
        
        /// <summary>
        /// Getter Method for checking connection state
        /// </summary>
        /// <returns>true is connected</returns>
        public Boolean BT_is_Connected()
        {
            return is_BT_Connected;
        }

        /// <summary>
        /// Method that receives message from thread
        /// </summary>
        /// <param name="id">ID of message</param>
        /// <param name="code">Code of message</param>
        /// <param name="data">message</param>
        public void ResultCallback(int id, int code, String data)
        {
            if (scTerminal.Get_encryption_state() && code != 99)
            {
                data = cryptLib.decrypt(data, AES_KEY, AES_IV);
            }

            collection_Messages tmp = new collection_Messages(0, code, data);
            newMessageCallback(tmp);

            Console.WriteLine("ID: " + id + ", CODE: " + code + ", MESSAGE: " + data);

            switch (code)
            {
                case 1:
                    switch (data)
                    {
                        case "application_stop":
                            is_BT_Connected = false;                             
                            StopBTConnection();
                            break;
                        case "smartcard_discovered":
                            Console.WriteLine("smartcardDiscovered");
                            break;
                        case "smartcard_connection_refused":
                            Console.WriteLine("Smartcard connection refused.");
                            break;
                        case "smartcard_connected":
                            Console.WriteLine("smartcardConnected");
                            break;
                        case "smartcard_disconnected":
                            Console.WriteLine("smartcardDisconnected");
                            break;
                        default:
                            Console.WriteLine("Error Receiving Message");
                            is_BT_Connected = false;
                            StopBTConnection();
                            break;
                    }
                    break;
            }
        }

    // END OF HELPER_BLUETOOTH CLASS !!!!        
    }

    /// <summary>
    /// Delegate for sending message to Bluetooth Class from thread
    /// </summary>
    /// <param name="id">ID of message</param>
    /// <param name="code">Code of message</param>
    /// <param name="data">message</param>
    public delegate void receiveCallback(int id, int code, String message);

    /// <summary>
    /// Class with thread which receives all messages
    /// </summary>
    public class AcceptMessages
    {
        private BluetoothClient client;
        private StreamReader wtr_2;
        private receiveCallback receiveDelegate;

        /// <summary>
        /// Constructor that init the Streamreader for receiving messages
        /// </summary>
        /// <param name="client">BluetoothClient instance</param>
        /// <param name="receiveDelegate">Delegate object for sending message to BluetoothClient class</param>
        public AcceptMessages(BluetoothClient client, receiveCallback receiveDelegate)
        {
            this.client = client;
            this.receiveDelegate = receiveDelegate;

            Stream peerStream = client.GetStream();
            wtr_2 = new StreamReader(peerStream);
        }

        /// <summary>
        /// Thread Method that checks for new messages
        /// </summary>
        public void CheckForMessages()
        {
            try
            {
                while (!wtr_2.EndOfStream)
                {
                    String msg = wtr_2.ReadLine();

                    String[] stringSeparators = new String[] { ">>" };
                    String[] sepString = msg.Split(stringSeparators, StringSplitOptions.None);

                    if (Int32.Parse(sepString[0]) == 0)
                    {
                        receiveDelegate(0, Int32.Parse(sepString[1]), sepString[2]);
                    }
                    else
                    {
                        collection_Messages cMessages = new collection_Messages(Int32.Parse(sepString[0]), Int32.Parse(sepString[1]), sepString[2]);
                        Messages_List._Messages.Add(cMessages);
                    }
                }
            }
            catch(Exception e)
            {
                receiveDelegate(0, 99, "Connection_Refused");
                wtr_2.Close();
            }      
        }
    }
}

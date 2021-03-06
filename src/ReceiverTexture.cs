﻿//using NAudio.Wave;
using NewTek;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
//using System.Windows;
//using System.Windows.Controls;
//using System.Windows.Media;
//using System.Windows.Media.Imaging;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;

using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;

using System.Drawing;

using VL.Core;
using VL.Lib.Basics.Imaging;
using ImagingPixelFormat = VL.Lib.Basics.Imaging.PixelFormat;
using VL.Lib.Basics.Resources;

//namespace NewTek.NDI.VL
namespace VL.IO.NDI
{
    // If you do not use this control, you can remove this file
    // and remove the dependency on naudio.
    // Alternatively you can also remove any naudio related entries
    // and use it for video only, but don't forget that you will still need
    // to free any audio frames received.
    public class ReceiverTexture : IDisposable, INotifyPropertyChanged
    {
        readonly IResourceProvider<Device> deviceProvider;

        #region private properties
        // a pointer to our unmanaged NDI receiver instance
        private IntPtr _recvInstancePtr = IntPtr.Zero;

        // a thread to receive frames on so that the UI is still functional
        private Thread _receiveThread = null;

        // a way to exit the thread safely
        private bool _exitThread = false;

        private IntPtr buffer0 = IntPtr.Zero;
        private IntPtr buffer1 = IntPtr.Zero;
        private int buffer01Size = 0;

        // should we send audio to Windows or not?
        private bool _audioEnabled = false;

        ////// the NAudio related
        ////private WasapiOut _wasapiOut = null;
        //private MultiplexingWaveProvider _multiplexProvider = null;
        //private BufferedWaveProvider _bufferedProvider = null;

        //// The last WaveFormat we used.
        //// This may change over time, so remember how we are configured currently.
        //private WaveFormat _waveFormat = null;

        //// the current audio volume
        //private float _volume = 1.0f;

        private bool _isPtz = false;
        private bool _canRecord = false;
        private String _webControlUrl = String.Empty;
        private String _receiverName = String.Empty;

        private Source _connectedSource;

        private readonly Subject<Texture2D> videoFrames = new Subject<Texture2D>();

        private Texture2D outputTexture;
        private Texture2DDescription textureDesc;

        private bool _videoEnabled = true;

        #endregion

        #region public properties
        /// <summary>
        /// The name of this receiver channel. Required or else an invalid argument exception will be thrown.
        /// </summary>
        public String ReceiverName
        {
            get { return _receiverName; }
            set { _receiverName = value; }
        }

        /// <summary>
        /// The NDI source to connect to. An empty new Source() or a Source with no Name will disconnect.
        /// </summary>
        public Source ConnectedSource
        {
            get { return _connectedSource; }
            set { _connectedSource = value; }
        }


        /// <summary>
        /// If true (default) received audio will be sent to the default Windows audio playback device.
        /// </summary>
        public bool IsAudioEnabled
        {
            get { return _audioEnabled; }
            set
            {
                if (value != _audioEnabled)
                {
                    NotifyPropertyChanged("IsAudioEnabled");
                }
            }
        }

        /// <summary>
        /// If true (default) received video will be sent to the screen.
        /// </summary>
        public bool IsVideoEnabled
        {
            get { return _videoEnabled; }
            set
            {
                if (value != _videoEnabled)
                {
                    NotifyPropertyChanged("IsVideoEnabled");
                }
            }
        }

        //[Category("NewTek NDI"),
        //Description("Set or get the current audio volume. Range is 0.0 to 1.0")]
        //public float Volume
        //{
        //    get { return _volume; }
        //    set
        //    {
        //        if (value != _volume)
        //        {
        //            _volume = Math.Max(0.0f, Math.Min(1.0f, value));

        //            if (_wasapiOut != null)
        //                _wasapiOut.Volume = _volume;

        //            NotifyPropertyChanged("Volume");
        //        }
        //    }
        //}

        /// <summary>
        /// Does the current source support PTZ functionality?
        /// </summary>
        public bool IsPtz
        {
            get { return _isPtz; }
            set
            {
                if (value != _isPtz)
                {
                    NotifyPropertyChanged("IsPtz");
                }
            }
        }

        /// <summary>
        /// Does the current source support record functionality?
        /// </summary>
        public bool IsRecordingSupported
        {
            get { return _canRecord; }
            set
            {
                if (value != _canRecord)
                {
                    NotifyPropertyChanged("IsRecordingSupported");
                }
            }
        }

        /// <summary>
        /// The web control URL for the current device, as a String, or an Empty String if not supported.
        /// </summary>
        public String WebControlUrl
        {
            get { return _webControlUrl; }
            set
            {
                if (value != _webControlUrl)
                {
                    NotifyPropertyChanged("WebControlUrl");
                }
            }
        }
        /// <summary>
        /// What and Why do we need This?
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Received Textures
        /// </summary>
        public IObservable<Texture2D> Frames => videoFrames;
        #endregion        
        

        public ReceiverTexture(NodeContext nodeContext)
        {
            deviceProvider = nodeContext.Factory.CreateService<IResourceProvider<Device>>(nodeContext);

        }

        #region PTZ Methods
        public bool SetPtzZoom(double value)
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_ptz_zoom(_recvInstancePtr, (float)value);
        }

        public bool SetPtzZoomSpeed(double value)
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_ptz_zoom_speed(_recvInstancePtr, (float)value);
        }

        public bool SetPtzPanTilt(double pan, double tilt)
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_ptz_pan_tilt(_recvInstancePtr, (float)pan, (float)tilt);
        }

        public bool SetPtzPanTiltSpeed(double panSpeed, double tiltSpeed)
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_ptz_pan_tilt_speed(_recvInstancePtr, (float)panSpeed, (float)tiltSpeed);
        }

        public bool PtzStorePreset(int index)
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero || index < 0 || index > 99)
                return false;

            return NDIlib.recv_ptz_store_preset(_recvInstancePtr, index);
        }

        public bool PtzRecallPreset(int index, double speed)
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero || index < 0 || index > 99)
                return false;

            return NDIlib.recv_ptz_recall_preset(_recvInstancePtr, index, (float)speed);
        }

        public bool PtzAutoFocus()
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_ptz_auto_focus(_recvInstancePtr);
        }

        public bool SetPtzFocusSpeed(double speed)
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_ptz_focus_speed(_recvInstancePtr, (float)speed);
        }

        public bool PtzWhiteBalanceAuto()
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_ptz_white_balance_auto(_recvInstancePtr);
        }

        public bool PtzWhiteBalanceIndoor()
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_ptz_white_balance_indoor(_recvInstancePtr);
        }

        public bool PtzWhiteBalanceOutdoor()
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_ptz_white_balance_outdoor(_recvInstancePtr);
        }

        public bool PtzWhiteBalanceOneShot()
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_ptz_white_balance_oneshot(_recvInstancePtr);
        }

        public bool PtzWhiteBalanceManual(double red, double blue)
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_ptz_white_balance_manual(_recvInstancePtr, (float)red, (float)blue);
        }

        public bool PtzExposureAuto()
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_ptz_exposure_auto(_recvInstancePtr);
        }

        public bool PtzExposureManual(double level)
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_ptz_exposure_manual(_recvInstancePtr, (float)level);
        }

        #endregion PTZ Methods

        #region Recording Methods

        /// <summary>
        /// This will start recording.If the recorder was already recording then the message is ignored. A filename is passed in as a ‘hint’.Since the recorder might
        /// already be recording (or might not allow complete flexibility over its filename), the filename might or might not be used.If the filename is empty, or
        /// not present, a name will be chosen automatically. 
        /// </summary>
        /// <param name="filenameHint">
        /// A filename is passed in as a ‘hint’.Since the recorder might already be recording (or might not allow complete flexibility over its filename), the filename 
        /// might or might not be used.If the filename is empty, or not present, a name will be chosen automatically.
        /// </param>
        /// <returns>true if recording started</returns>
        public bool RecordingStart(String filenameHint = "")
        {
            if (!_canRecord || _recvInstancePtr == IntPtr.Zero)
                return false;

            bool retVal = false;

            if (String.IsNullOrEmpty(filenameHint))
            {
                retVal = NDIlib.recv_recording_start(_recvInstancePtr, IntPtr.Zero);
            }
            else
            {
                // convert to an unmanaged UTF8 IntPtr
                IntPtr fileNamePtr = UTF.StringToUtf8(filenameHint);

                retVal = NDIlib.recv_recording_start(_recvInstancePtr, IntPtr.Zero);

                // don't forget to free it
                Marshal.FreeHGlobal(fileNamePtr);
            }

            return retVal;
        }

        /// <summary>
        /// Stop recording.
        /// </summary>
        /// <returns></returns>
        public bool RecordingStop()
        {
            if (!_canRecord || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_recording_stop(_recvInstancePtr);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="level"></param>
        /// <returns></returns>
        public bool RecordingSetAudioLevel(double level)
        {
            if (!_canRecord || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_recording_set_audio_level(_recvInstancePtr, (float)level);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public bool IsRecording()
        {
            if (!_canRecord || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_recording_is_recording(_recvInstancePtr);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public String GetRecordingFilename()
        {
            if (!_canRecord || _recvInstancePtr == IntPtr.Zero)
                return String.Empty;

            IntPtr filenamePtr = NDIlib.recv_recording_get_filename(_recvInstancePtr);
            if (filenamePtr == IntPtr.Zero)
            {
                return String.Empty;
            }
            else
            {
                String filename = UTF.Utf8ToString(filenamePtr);

                // free it
                NDIlib.recv_free_string(_recvInstancePtr, filenamePtr);

                return filename;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns>Error as String</returns>
        public String GetRecordingError()
        {
            if (!_canRecord || _recvInstancePtr == IntPtr.Zero)
                return String.Empty;

            IntPtr errorPtr = NDIlib.recv_recording_get_error(_recvInstancePtr);
            if (errorPtr == IntPtr.Zero)
            {
                return String.Empty;
            }
            else
            {
                String error = UTF.Utf8ToString(errorPtr);

                // free it
                NDIlib.recv_free_string(_recvInstancePtr, errorPtr);

                return error;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="recordingTimes"></param>
        /// <returns></returns>
        public bool GetRecordingTimes(ref NDIlib.recv_recording_time_t recordingTimes)
        {
            if (!_canRecord || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_recording_get_times(_recvInstancePtr, ref recordingTimes);
        }

        #endregion Recording Methods

        private void NotifyPropertyChanged(String info)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(info));
            }
        }

        #region dispose and finalize
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // tell the thread to exit
                    _exitThread = true;

                    // wait for it to exit
                    if (_receiveThread != null)
                    {
                        _receiveThread.Join();

                        _receiveThread = null;
                    }

                    //// Stop the audio device if needed
                    //if (_wasapiOut != null)
                    //{
                    //    _wasapiOut.Stop();
                    //    _wasapiOut.Dispose();
                    //    _wasapiOut = null;
                    //}


                    videoFrames.Dispose();
                    outputTexture.Dispose();

                }

                // Destroy the receiver
                if (_recvInstancePtr != IntPtr.Zero)
                {
                    NDIlib.recv_destroy(_recvInstancePtr);
                    _recvInstancePtr = IntPtr.Zero;
                }

                // Not required, but "correct". (see the SDK documentation)
                NDIlib.destroy();

                Marshal.FreeCoTaskMem(buffer0);
                Marshal.FreeCoTaskMem(buffer1);


                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~ReceiverTexture()
        {
            Dispose(false);
        }

        private bool _disposed = false;
        #endregion dispose and finalize

        /// <summary>
        /// when the ConnectedSource changes, connect to it.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void OnConnectedSourceChanged(object sender, EventArgs e)
        {
            ReceiverTexture s = sender as ReceiverTexture;
            if (s == null)
                return;

            s.Connect(s.ConnectedSource);
        }

        /// <summary>
        /// connect to an NDI source in our Dictionary by name
        /// </summary>
        /// <param name="source"></param>
        /// <param name="colorFormat"></param>
        /// <param name="bandwidth"></param>
        /// <param name="allowVideoFields"></param>
        public void Connect(Source source, 
            NDIlib.recv_color_format_e colorFormat = NDIlib.recv_color_format_e.recv_color_format_BGRX_BGRA,
            NDIlib.recv_bandwidth_e bandwidth = NDIlib.recv_bandwidth_e.recv_bandwidth_highest, 
            bool allowVideoFields = false)
        {
            //if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            //    return;

            if (String.IsNullOrEmpty(ReceiverName))
                throw new ArgumentException("sourceName can not be null or empty.", ReceiverName);

            // just in case we're already connected
            Disconnect();

            //// before we are connected, we need to set up our image
            //// it's bad practice to do this in the constructor
            //if (Child == null)
            //    Child = VideoSurface;

            // Sanity
            if (source == null || String.IsNullOrEmpty(source.Name))
                return;

            // a source_t to describe the source to connect to.
            NDIlib.source_t source_t = new NDIlib.source_t()
            {
                p_ndi_name = UTF.StringToUtf8(source.Name)
            };
            
            // make a description of the receiver we want
            NDIlib.recv_create_v3_t recvDescription = new NDIlib.recv_create_v3_t()
            {
                // the source we selected
                source_to_connect_to = source_t,

                // we want BGRA frames for this example
                color_format = colorFormat,
                
                // we want full quality - for small previews or limited bandwidth, choose lowest
                bandwidth = bandwidth,

                // let NDIlib deinterlace for us if needed
                allow_video_fields = allowVideoFields,

                // The name of the NDI receiver to create. This is a NULL terminated UTF8 string and should be
                // the name of receive channel that you have. This is in many ways symettric with the name of
                // senders, so this might be "Channel 1" on your system.
                p_ndi_recv_name = UTF.StringToUtf8(ReceiverName)
            };

            // create a new instance connected to this source
            _recvInstancePtr = NDIlib.recv_create_v3(ref recvDescription);

            // free the memory we allocated with StringToUtf8
            Marshal.FreeHGlobal(source_t.p_ndi_name);
            Marshal.FreeHGlobal(recvDescription.p_ndi_recv_name = UTF.StringToUtf8(ReceiverName));

            // did it work?
            System.Diagnostics.Debug.Assert(_recvInstancePtr != IntPtr.Zero, "Failed to create NDI receive instance.");

            if (_recvInstancePtr != IntPtr.Zero)
            {
                // We are now going to mark this source as being on program output for tally purposes (but not on preview)
                SetTallyIndicators(true, false);

                // start up a thread to receive on
                _receiveThread = new Thread(ReceiveThreadProc) { IsBackground = true, Name = "NdiExampleReceiveThread" };
                _receiveThread.Start();
            }

            //Frames = Observable.Create<Texture2D>((observer, token) => PlayUrl(Url, observer, token)).Publish().RefCount();
        }

        public void Disconnect()
        {
            // in case we're connected, reset the tally indicators
            SetTallyIndicators(false, false);

            // check for a running thread
            if (_receiveThread != null)
            {
                // tell it to exit
                _exitThread = true;

                // wait for it to end
                _receiveThread.Join();
            }

            // reset thread defaults
            _receiveThread = null;
            _exitThread = false;

            // Destroy the receiver
            NDIlib.recv_destroy(_recvInstancePtr);

            // set it to a safe value
            _recvInstancePtr = IntPtr.Zero;

            // set function status to defaults
            IsPtz = false;
            IsRecordingSupported = false;
            WebControlUrl = String.Empty;
        }

        public void SetTallyIndicators(bool onProgram, bool onPreview)
        {
            // we need to have a receive instance
            if (_recvInstancePtr != IntPtr.Zero)
            {
                // set up a state descriptor
                NDIlib.tally_t tallyState = new NDIlib.tally_t()
                {
                    on_program = onProgram,
                    on_preview = onPreview
                };

                // set it on the receiver instance
                NDIlib.recv_set_tally(_recvInstancePtr, ref tallyState);
            }
        }

        /// <summary>
        /// the receive thread runs though this loop until told to exit
        /// </summary>
        void ReceiveThreadProc()
        {
            bool newVideo = true;
            using var deviceHandle = deviceProvider.GetHandle();
            var device = deviceHandle.Resource;

            while (!_exitThread && _recvInstancePtr != IntPtr.Zero)
            {
                

                // The descriptors
                NDIlib.video_frame_v2_t videoFrame = new NDIlib.video_frame_v2_t();
                NDIlib.audio_frame_v2_t audioFrame = new NDIlib.audio_frame_v2_t();
                NDIlib.metadata_frame_t metadataFrame = new NDIlib.metadata_frame_t();

                switch (NDIlib.recv_capture_v2(_recvInstancePtr, ref videoFrame, ref audioFrame, ref metadataFrame, 1000))
                {
                    // No data
                    case NDIlib.frame_type_e.frame_type_none:
                        // No data received
                        break;

                    // frame settings - check for extended functionality
                    case NDIlib.frame_type_e.frame_type_status_change:
                        // check for PTZ
                        IsPtz = NDIlib.recv_ptz_is_supported(_recvInstancePtr);

                        // Check for recording
                        IsRecordingSupported = NDIlib.recv_recording_is_supported(_recvInstancePtr);

                        // Check for a web control URL
                        // We must free this string ptr if we get one.
                        IntPtr webUrlPtr = NDIlib.recv_get_web_control(_recvInstancePtr);
                        if (webUrlPtr == IntPtr.Zero)
                        {
                            WebControlUrl = String.Empty;
                        }
                        else
                        {
                            // convert to managed String
                            WebControlUrl = UTF.Utf8ToString(webUrlPtr);

                            // Don't forget to free the string ptr
                            NDIlib.recv_free_string(_recvInstancePtr, webUrlPtr);
                        }

                        break;

                    // Video data
                    case NDIlib.frame_type_e.frame_type_video:

                        // if not enabled, just discard
                        // this can also occasionally happen when changing sources
                        if (!_videoEnabled || videoFrame.p_data == IntPtr.Zero)
                        {
                            // alreays free received frames
                            NDIlib.recv_free_video_v2(_recvInstancePtr, ref videoFrame);

                            break;
                        }

                        // get all our info so that we can free the frame
                        int yres = (int)videoFrame.yres;
                        int xres = (int)videoFrame.xres;

                        // quick and dirty aspect ratio correction for non-square pixels - SD 4:3, 16:9, etc.
                        double dpiX = 96.0 * (videoFrame.picture_aspect_ratio / ((double)xres / (double)yres));

                        int stride = (int)videoFrame.line_stride_in_bytes;
                        int bufferSize = yres * stride;


                        if(bufferSize != buffer01Size)
                        {
                            buffer0 = Marshal.ReAllocCoTaskMem(buffer0, bufferSize);
                            buffer1 = Marshal.ReAllocCoTaskMem(buffer1, bufferSize);
                            buffer01Size = bufferSize;
                        }

                        // Copy data
                        unsafe
                        {
                            byte* dst = (byte*)buffer0.ToPointer() ;
                            byte* src = (byte*)videoFrame.p_data.ToPointer();

                            for (int y = 0; y < yres; y++)
                            {
                                memcpy(dst, src, stride);
                                dst += stride;
                                src += stride;
                            }
                        }

                        // swap
                        IntPtr temp = buffer0;
                        buffer0 = buffer1;
                        buffer1 = temp;

                        SharpDX.DXGI.Format texFmt;
                        switch (videoFrame.FourCC)
                        {
                            case NDIlib.FourCC_type_e.FourCC_type_BGRA:
                                texFmt = SharpDX.DXGI.Format.B8G8R8A8_UNorm;
                                break;
                            case NDIlib.FourCC_type_e.FourCC_type_BGRX:
                                texFmt = SharpDX.DXGI.Format.B8G8R8A8_UNorm;
                                break;
                            case NDIlib.FourCC_type_e.FourCC_type_RGBA:
                                texFmt = SharpDX.DXGI.Format.R8G8B8A8_UNorm;
                                break;
                            case NDIlib.FourCC_type_e.FourCC_type_RGBX:
                                texFmt = SharpDX.DXGI.Format.B8G8R8A8_UNorm;
                                break;
                            default:
                                texFmt = SharpDX.DXGI.Format.Unknown; // TODO: need to handle other video formats
                                break;
                        }

                        if (newVideo) // it's the first time we enter the while loop, so cerate a new texture
                        {
                            textureDesc = new Texture2DDescription()
                            {
                                Width = xres,
                                Height = yres,
                                MipLevels = 1,
                                ArraySize = 1,
                                Format = texFmt,
                                SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                                Usage = ResourceUsage.Default,
                                BindFlags = BindFlags.ShaderResource,
                                CpuAccessFlags = CpuAccessFlags.None,
                                OptionFlags = ResourceOptionFlags.None
                            };

                            outputTexture = new Texture2D(device, textureDesc);

                            newVideo = false;
                        }

                        try
                        {
                            DataBox srcBox = new DataBox(buffer1);
                            device.ImmediateContext.UpdateSubresource(srcBox, outputTexture, 0);

                            videoFrames.OnNext(outputTexture);
                        }
                        finally
                        {
                            device.ImmediateContext.UnmapSubresource(outputTexture, 0);
                        }


                        // free frames that were received AFTER use!
                        // This writepixels call is dispatched, so we must do it inside this scope.
                        NDIlib.recv_free_video_v2(_recvInstancePtr, ref videoFrame);
                       
                        break;

                    // audio is beyond the scope of this example
                    case NDIlib.frame_type_e.frame_type_audio:

                        // if no audio or disabled, nothing to do
                        if (!_audioEnabled || audioFrame.p_data == IntPtr.Zero || audioFrame.no_samples == 0)
                        {
                            // alreays free received frames
                            NDIlib.recv_free_audio_v2(_recvInstancePtr, ref audioFrame);

                            break;
                        }

                        //// if the audio format changed, we need to reconfigure the audio device
                        //bool formatChanged = false;

                        //// make sure our format has been created and matches the incomming audio
                        //if (_waveFormat == null ||
                        //    _waveFormat.Channels != audioFrame.no_channels ||
                        //    _waveFormat.SampleRate != audioFrame.sample_rate)
                        //{
                        //    //// Create a wavformat that matches the incomming frames
                        //    //_waveFormat = WaveFormat.CreateIeeeFloatWaveFormat((int)audioFrame.sample_rate, (int)audioFrame.no_channels);

                        //    formatChanged = true;
                        //}
                        
                        //// set up our audio buffer if needed
                        //if (_bufferedProvider == null || formatChanged)
                        //{
                        //    _bufferedProvider = new BufferedWaveProvider(_waveFormat);
                        //    _bufferedProvider.DiscardOnBufferOverflow = true;
                        //}

                        //// set up our multiplexer used to mix down to 2 output channels)
                        //if (_multiplexProvider == null || formatChanged)
                        //{
                        //    _multiplexProvider = new MultiplexingWaveProvider(new List<IWaveProvider>() { _bufferedProvider }, 2);
                        //}

                        //    // set up our audio output device
                        //    if (_wasapiOut == null || formatChanged)
                        //    {
                        //        // We can't guarantee audio sync or buffer fill, that's beyond the scope of this example.
                        //        // This is close enough to show that audio is received and converted correctly.
                        //        _wasapiOut = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 50);
                        //        _wasapiOut.Init(_multiplexProvider);
                        //        _wasapiOut.Volume = _volume;
                        //        _wasapiOut.Play();
                        //    }

                        // we're working in bytes, so take the size of a 32 bit sample (float) into account
                        int sizeInBytes = (int)audioFrame.no_samples * (int)audioFrame.no_channels * sizeof(float);

                        // NAudio is expecting interleaved audio and NDI uses planar.
                        // create an interleaved frame and convert from the one we received
                        NDIlib.audio_frame_interleaved_32f_t interleavedFrame = new NDIlib.audio_frame_interleaved_32f_t()
                        {
                            sample_rate = audioFrame.sample_rate,
                            no_channels = audioFrame.no_channels,
                            no_samples = audioFrame.no_samples,
                            timecode = audioFrame.timecode
                        };

                        // we need a managed byte array to add to buffered provider
                        byte[] audBuffer = new byte[sizeInBytes];

                        // pin the byte[] and get a GC handle to it
                        // doing it this way saves an expensive Marshal.Alloc/Marshal.Copy/Marshal.Free later
                        // the data will only be moved once, during the fast interleave step that is required anyway
                        GCHandle handle = GCHandle.Alloc(audBuffer, GCHandleType.Pinned);

                        // access it by an IntPtr and use it for our interleaved audio buffer
                        interleavedFrame.p_data = handle.AddrOfPinnedObject();

                        // Convert from float planar to float interleaved audio
                        // There is a matching version of this that converts to interleaved 16 bit audio frames if you need 16 bit
                        NDIlib.util_audio_to_interleaved_32f_v2(ref audioFrame, ref interleavedFrame);

                        // release the pin on the byte[]
                        // never try to access p_data after the byte[] has been unpinned!
                        // that IntPtr will no longer be valid.
                        handle.Free();

                        //// push the byte[] buffer into the bufferedProvider for output
                        //_bufferedProvider.AddSamples(audBuffer, 0, sizeInBytes);

                        // free the frame that was received
                        NDIlib.recv_free_audio_v2(_recvInstancePtr, ref audioFrame);

                        break;
                    // Metadata
                    case NDIlib.frame_type_e.frame_type_metadata:

                        // UTF-8 strings must be converted for use - length includes the terminating zero
                        //String metadata = Utf8ToString(metadataFrame.p_data, metadataFrame.length-1);

                        //System.Diagnostics.Debug.Print(metadata);

                        // free frames that were received
                        NDIlib.recv_free_metadata(_recvInstancePtr, ref metadataFrame);
                        break;
                }
            }
        }

        [DllImport("msvcrt.dll", EntryPoint = "memcpy", CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public unsafe static extern IntPtr memcpy(byte* dest, byte* src, int count);
    }
}

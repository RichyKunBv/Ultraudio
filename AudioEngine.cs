using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ManagedBass;
using ManagedBass.Flac;

namespace Ultraudio
{
    public class DeviceModel
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public bool IsDefault { get; set; }
        public override string ToString() => Name;
    }

    public class AudioEngine
    {
        private int _stream;
        private bool _deviceInitialized = false;
        private int _deviceSampleRate = 44100;
        private int _currentDevice = -1;
        private GCHandle _memoryHandle; // To keep memory pinned during playback
        
        public event EventHandler? TrackEnded;
        private SyncProcedure _endSync;

        public AudioEngine()
        {
            _endSync = new SyncProcedure(OnTrackEnd);
            // Cargar el plugin DSD si existe
            try
            {
                Bass.PluginLoad("libbassdsd.dylib");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Nota: No se pudo cargar libbassdsd.dylib: {ex.Message}");
            }
        }

        public List<DeviceModel> ObtenerDispositivos()
        {
            var list = new List<DeviceModel>();
            int deviceCount = Bass.DeviceCount;
            for (int i = 1; i < deviceCount; i++) // 0 is 'no sound'
            {
                var info = Bass.GetDeviceInfo(i);
                if (info.IsEnabled)
                {
                    list.Add(new DeviceModel { Index = i, Name = info.Name, IsDefault = info.IsDefault });
                }
            }
            return list;
        }

        public bool InicializarDispositivo(int deviceIndex = -1, int sampleRate = 44100)
        {
            try
            {
                if (_deviceInitialized && (_deviceSampleRate != sampleRate || _currentDevice != deviceIndex))
                {
                    Bass.Free();
                    _deviceInitialized = false;
                }

                if (!_deviceInitialized)
                {
                    bool init = Bass.Init(deviceIndex, sampleRate, DeviceInitFlags.Latency);

                    if (!init && Bass.LastError == Errors.Already)
                    {
                        init = true;
                    }

                    _deviceInitialized = init;
                    if (init)
                    {
                        _deviceSampleRate = sampleRate;
                        _currentDevice = deviceIndex;
                    }
                    return init;
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Excepción inicializando dispositivo: {ex}");
                return false;
            }
        }

        public void CambiarDispositivo(int deviceIndex)
        {
            if (deviceIndex == _currentDevice) return;
            
            // Pausar y mover el stream si existe
            bool wasPlaying = _stream != 0 && Bass.ChannelIsActive(_stream) == PlaybackState.Playing;
            
            InicializarDispositivo(deviceIndex, _deviceSampleRate);
            
            if (_stream != 0)
            {
                Bass.ChannelSetDevice(_stream, deviceIndex);
                if (wasPlaying) Bass.ChannelPlay(_stream);
            }
        }

        public void Reproducir(string rutaArchivo, bool memoryPlayback = false)
        {
            LiberarStream();

            // Determinar tipo de archivo por extensión
            string ext = Path.GetExtension(rutaArchivo).ToLower();
            bool isFlac = ext == ".flac";

            // Inspeccionar info
            int infoStream = isFlac 
                ? BassFlac.CreateStream(rutaArchivo, 0, 0, BassFlags.Decode)
                : Bass.CreateStream(rutaArchivo, 0, 0, BassFlags.Decode);

            float freqf = 44100f;
            if (infoStream != 0)
            {
                Bass.ChannelGetAttribute(infoStream, ChannelAttribute.Frequency, out freqf);
                Bass.StreamFree(infoStream);
            }
            int fileRate = Math.Max(8000, Math.Min(384000, (int)Math.Round(freqf)));

            // Ajustar dispositivo para bit-perfect
            if (!_deviceInitialized || _deviceSampleRate != fileRate)
            {
                if (!InicializarDispositivo(_currentDevice, fileRate))
                {
                    InicializarDispositivo(_currentDevice, 44100);
                }
            }

            // Crear stream
            if (memoryPlayback)
            {
                byte[] fileBytes = File.ReadAllBytes(rutaArchivo);
                _memoryHandle = GCHandle.Alloc(fileBytes, GCHandleType.Pinned);
                
                _stream = isFlac 
                    ? BassFlac.CreateStream(_memoryHandle.AddrOfPinnedObject(), 0, fileBytes.Length, BassFlags.Default)
                    : Bass.CreateStream(_memoryHandle.AddrOfPinnedObject(), 0, fileBytes.Length, BassFlags.Default);
            }
            else
            {
                _stream = isFlac 
                    ? BassFlac.CreateStream(rutaArchivo, 0, 0, BassFlags.Default)
                    : Bass.CreateStream(rutaArchivo, 0, 0, BassFlags.Default);
            }

            if (_stream != 0)
            {
                Bass.ChannelSetSync(_stream, SyncFlags.End, 0, _endSync);
                Bass.ChannelPlay(_stream);
            }
        }

        private void OnTrackEnd(int handle, int channel, int data, IntPtr user)
        {
            TrackEnded?.Invoke(this, EventArgs.Empty);
        }

        public void Detener()
        {
            if (_stream != 0)
            {
                Bass.ChannelStop(_stream);
            }
        }

        public void AlternarPausa()
        {
            if (_stream != 0)
            {
                if (Bass.ChannelIsActive(_stream) == PlaybackState.Playing)
                    Bass.ChannelPause(_stream);
                else
                    Bass.ChannelPlay(_stream);
            }
        }

        private void LiberarStream()
        {
            if (_stream != 0)
            {
                Bass.StreamFree(_stream);
                _stream = 0;
            }
            if (_memoryHandle.IsAllocated)
            {
                _memoryHandle.Free();
            }
        }

        public void Liberar()
        {
            LiberarStream();
            Bass.Free();
            _deviceInitialized = false;
        }

        // --- Nuevas propiedades ---
        public double PosicionSegundos
        {
            get => _stream != 0 ? Bass.ChannelBytes2Seconds(_stream, Bass.ChannelGetPosition(_stream)) : 0;
            set
            {
                if (_stream != 0)
                {
                    Bass.ChannelSetPosition(_stream, Bass.ChannelSeconds2Bytes(_stream, value));
                }
            }
        }

        public double DuracionSegundos => _stream != 0 ? Bass.ChannelBytes2Seconds(_stream, Bass.ChannelGetLength(_stream)) : 0;

        public double Volumen
        {
            get
            {
                if (_stream == 0) return 1.0;
                Bass.ChannelGetAttribute(_stream, ChannelAttribute.Volume, out float vol);
                return vol;
            }
            set
            {
                if (_stream != 0)
                {
                    Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, (float)Math.Clamp(value, 0.0, 1.0));
                }
            }
        }
    }
}

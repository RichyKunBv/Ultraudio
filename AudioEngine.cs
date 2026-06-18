using System;
using ManagedBass;
using ManagedBass.Flac; // <- Esta es la línea que faltaba

namespace Ultraudio // Ajustado a tu nombre de proyecto
{
    public class AudioEngine
    {
        private int _stream;

        public bool InicializarDispositivo()
        {
            bool init = Bass.Init(-1, 44100, DeviceInitFlags.Latency);

            if (!init)
            {
                // BASS devuelve Errors.Already si el dispositivo ya estaba inicializado.
                // En ese caso considerar la inicialización como idempotente (éxito).
                if (Bass.LastError == Errors.Already)
                {
                    init = true;
                }
                else
                {
                    Console.WriteLine($"Error al inicializar el hardware de audio: {Bass.LastError}");
                }
            }

            return init;
        }

        public void ReproducirFlac(string rutaArchivo)
        {
            if (_stream != 0)
            {
                Bass.StreamFree(_stream);
            }

            _stream = BassFlac.CreateStream(rutaArchivo, 0, 0, BassFlags.Default);

            if (_stream != 0)
            {
                Bass.ChannelPlay(_stream);
            }
            else
            {
                Console.WriteLine($"Error al cargar el archivo FLAC: {Bass.LastError}");
            }
        }

        public void Detener()
        {
            if (_stream != 0)
            {
                Bass.ChannelStop(_stream);
            }
        }

        public void Liberar()
        {
            Bass.Free();
        }
    }
}
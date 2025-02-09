using System;

namespace Proxima
{
    public class ProximaStatus
    {
        // Is Proxima running?
        private bool _isRunning;
        public bool IsRunning => _isRunning;

        // Information for how to connect to Proxima.
        private string _connectInfo;
        public string ConnectInfo => _connectInfo;

        // True if Proxima is listening for connections.
        private bool _listening;
        public bool Listening => _listening;

        // Number of active connections which have entered the correct password.
        private int _connections;
        public int Connections => _connections;

        // Error message to display to the user.
        private string _error;
        public string Error => _error;

        // Event which is fired when any of the above properties change.
        public event Action Changed;

        internal void Reset()
        {
            _isRunning = false;
            _connectInfo = null;
            _listening = false;
            _connections = 0;
            _error = null;
            Changed?.Invoke();
        }

        internal void SetRunning(bool running)
        {
            _isRunning = running;
            Changed?.Invoke();
        }

        internal void SetConnectInfo(string info)
        {
            _connectInfo = info;
            _listening  = true;
            _error = null;
            Changed?.Invoke();
        }

        internal void SetError(string error)
        {
            _error = error;
            Changed?.Invoke();
        }

        internal void IncrementConnections()
        {
            _connections++;
            Changed?.Invoke();
        }

        internal void DecrementConnections()
        {
            if (_connections > 0)
            {
                _connections--;
                Changed?.Invoke();
            }
        }
    }
}
namespace Soha.Service
{
    public class ChainService
    {
        public delegate void NewHeads(ulong Heand);
        public event NewHeads NewHeadsEvent;

        private ulong _block;
        public ulong BlockNumber
        {
            get
            {
                return _block;
            }
            set
            {
                _block = value;
                NewHeadsEvent?.Invoke(value);
            }
        }
    }
}

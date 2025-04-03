namespace CycloneGames.Cheat
{
    public struct CheatCommand : VitalRouter.ICommand
    {
        public CheatCommand(string inID, string[] inParams)
        {
            ID = inID;
            Params = inParams;
        }
        public string ID { get; set; }
        public string[] Params { get; set; }
    }
}
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using UnityEngine.UI;

public class NetworkedServer : MonoBehaviour
{
    int maxConnections = 1000;
    int reliableChannelID;
    int unreliableChannelID;
    int hostID;
    int socketPort = 5497;

    LinkedList<PlayerAccount> playerAccounts;

    [SerializeField]
    List<connectionIDprofile> IDprofiles = new List<connectionIDprofile>();
    //connectionIDprofile[] IDprofiles;
    [SerializeField]
    connectionIDprofile structTest = new connectionIDprofile();

    LinkedList<GameRoom> gameRooms;

    const int PlayerAccountRecord = 1;

    string PlayerAccountFilePath;

    int playerWaitingForMatchID = -1;


    // Start is called before the first frame update
    void Start()
    {
        NetworkTransport.Init();
        ConnectionConfig config = new ConnectionConfig();
        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);
        HostTopology topology = new HostTopology(config, maxConnections);
        hostID = NetworkTransport.AddHost(topology, socketPort, null);

        playerAccounts = new LinkedList<PlayerAccount>();
        PlayerAccountFilePath = Application.dataPath + Path.DirectorySeparatorChar + "PlayerAccounts.txt";

        LoadPlayerAccounts();

        foreach(PlayerAccount pa in playerAccounts)
        {
            Debug.Log(pa.name + " " + pa.password);
        }

        gameRooms = new LinkedList<GameRoom>();

        //IDprofiles = new connectionIDprofile[1];
        //IDprofiles = new List<connectionIDprofile>();
    }

    // Update is called once per frame
    void Update()
    {

        int recHostID;
        int recConnectionID;
        int recChannelID;
        byte[] recBuffer = new byte[1024];
        int bufferSize = 1024;
        int dataSize;
        byte error = 0;

        NetworkEventType recNetworkEvent = NetworkTransport.Receive(out recHostID, out recConnectionID, out recChannelID, recBuffer, bufferSize, out dataSize, out error);

        switch (recNetworkEvent)
        {
            case NetworkEventType.Nothing:
                break;
            case NetworkEventType.ConnectEvent:
                Debug.Log("Connection, " + recConnectionID);
                break;
            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                ProcessRecievedMsg(msg, recConnectionID);
                //SendMessageToClient("Message Received", recConnectionID);
                break;
            case NetworkEventType.DisconnectEvent:
                Debug.Log("Disconnection, " + recConnectionID);
                break;
        }

    }
  
    public void SendMessageToClient(string msg, int id)
    {
        byte error = 0;
        byte[] buffer = Encoding.Unicode.GetBytes(msg);
        NetworkTransport.Send(hostID, id, reliableChannelID, buffer, msg.Length * sizeof(char), out error);
    }
    
    private void ProcessRecievedMsg(string msg, int id)
    {
        Debug.Log("msg recieved = " + msg + ".  connection id = " + id);

        string[] csv = msg.Split(',');

        int signifier = int.Parse(csv[0]);

        if(signifier == ClientToServerSignifiers.CreateAccount)
        {
            // check if player account name already exists
            string name = csv[1];
            string password = csv[2];

            bool nameIsInUse = false;
            foreach(PlayerAccount pa in playerAccounts)
            {
                if(pa.name == name)
                {
                    SendMessageToClient(ServertoClientSignifiers.CreateAccountFail + ",Account already exists", id);

                    Debug.Log("Account already exists");
                    nameIsInUse = true;
                }
            }

            if (!nameIsInUse)
            {
                PlayerAccount newPlayerAccount = new PlayerAccount(name, password);

                playerAccounts.AddLast(newPlayerAccount);
                SendMessageToClient(ServertoClientSignifiers.CreateAccountSuccess + ", Created Account", id);
                // save list to HD
                SavePlayerAccounts();
                Debug.Log("Created Account");
                // stores ID the player connects from
                newPlayerAccount.connectionID = id;
                connectionIDprofile IDprofile = new connectionIDprofile(id, newPlayerAccount.name);
                IDprofiles.Add(IDprofile);
            }
            // if not, create new account, add to list and save list to HD
            // send to client success/failure
        }
        else if (signifier == ClientToServerSignifiers.LoginAccount)
        {
            string name = csv[1];
            string pass = csv[2];

            bool userNameHasBeenFound = false;

            foreach (PlayerAccount pa in playerAccounts)
            {
                if (pa.name == name)
                {
                    userNameHasBeenFound = true;
                    if (pa.password == pass)
                    {
                        SendMessageToClient(ServertoClientSignifiers.LoginAccountSuccess + ",Login Account", id);
                        Debug.Log("Login Account");
                        // Stores the id the user connects from
                        pa.connectionID = id;
                        connectionIDprofile IDprofile = new connectionIDprofile(id, pa.name);
                        IDprofiles.Add(IDprofile);
                        Debug.Log(IDprofiles.Count);
                    }
                    else
                    {
                        SendMessageToClient(ServertoClientSignifiers.LoginAccountFail + ",Wrong password", id);
                        Debug.Log("Wrong password");
                    }
                }
            }

            if (!userNameHasBeenFound)
            {
                SendMessageToClient(ServertoClientSignifiers.LoginAccountFail + ",Username is not found", id);
                Debug.Log("Username is not found");
            }
            // Check if player account name already exists.
            // Send to client success/failure
        }
        else if (signifier == ClientToServerSignifiers.JoinQueueForGameRoom)
        {
            Debug.Log("we need to get this player into a waiting queue");
            if(playerWaitingForMatchID == -1)
            {
                playerWaitingForMatchID = id;
            }
            else
            {
                GameRoom gr = new GameRoom(playerWaitingForMatchID, id);
                gameRooms.AddLast(gr);
                SendMessageToClient(ServertoClientSignifiers.GameStart + "", gr.playerID2);
                SendMessageToClient(ServertoClientSignifiers.GameStart + "", gr.playerID1);

                playerWaitingForMatchID = -1;
            }
        }
        else if (signifier == ClientToServerSignifiers.TicTacToeShapeSelectPlay)
        {
            GameRoom gr = GetGameRoomWithClientID(id);

            if(gr != null)
            {
                if(gr.playerID1 == id)
                {
                    SendMessageToClient(ServertoClientSignifiers.OpponentPlay + "", gr.playerID2);
                }
                else if(gr.playerID2 == id)
                {
                    SendMessageToClient(ServertoClientSignifiers.OpponentPlay + "", gr.playerID1);
                }
            }
            // get the game room that the client ID is in

        }
        else if (signifier == ClientToServerSignifiers.ChatBoxMessageSend)
        {
            Debug.Log("Processing message");
            GameRoom gr = GetGameRoomWithClientID(id);
            string message = csv[1];

            if (gr != null)
            {
                string username = "Unknown";
                Debug.Log("Checking to see if id exists");
                foreach(connectionIDprofile ID in IDprofiles)
                {
                    if(ID.connectionID == id)
                    {
                        username = ID.profileName;
                    }
                }
                string msgList = ServertoClientSignifiers.chatBoxMessageReceive + "," + username + ": " + message 
                    + "," + ServertoClientSignifiers.chatReceivedTypePlayer;
                if (gr.playerID1 == id)
                {
                    SendMessageToClient(msgList, gr.playerID2);

                    SendtoAllObservers(gr, username, id, message, ServertoClientSignifiers.chatReceivedTypePlayer);
                }
                else if (gr.playerID2 == id)
                {
                    SendMessageToClient(msgList, gr.playerID1);

                    SendtoAllObservers(gr, username, id, message, ServertoClientSignifiers.chatReceivedTypePlayer);
                }
                else
                {
                    Debug.Log(username + " sent something");
                    msgList = ServertoClientSignifiers.chatBoxMessageReceive + "," + username + ": " + message 
                        + "," + ServertoClientSignifiers.chatReceivedTypeObserver;
                    SendMessageToClient(msgList, gr.playerID1);
                    SendMessageToClient(msgList, gr.playerID2);

                    SendtoAllObservers(gr, username, id, message, ServertoClientSignifiers.chatReceivedTypeObserver);
                }
            }
        }
        else if (signifier == ClientToServerSignifiers.JoinAsObserver)
        {
            if(gameRooms != null)
            {
                // Only joins the first gameroom as observer
                gameRooms.First.Value.observers.Add(id);
                SendMessageToClient(ServertoClientSignifiers.ObserverJoined + ",you are observing", id);
            }
        }
    }

    private void SavePlayerAccounts()
    {
        StreamWriter sw = new StreamWriter(PlayerAccountFilePath);

        foreach(PlayerAccount pa in playerAccounts)
        {
            sw.WriteLine(PlayerAccountRecord + "," + pa.name + "," + pa.password);
        }

        sw.Close();
    }

    private void LoadPlayerAccounts()
    {
        if (File.Exists(PlayerAccountFilePath))
        {
            StreamReader sr = new StreamReader(PlayerAccountFilePath);

            string line;

            while ((line = sr.ReadLine()) != null)
            {
                string[] csv = line.Split(',');

                int signifier = int.Parse(csv[0]);

                if (signifier == PlayerAccountRecord)
                {
                    PlayerAccount pa = new PlayerAccount(csv[1], csv[2]);
                    playerAccounts.AddLast(pa);
                }
            }

            sr.Close();
        }
    }

    private GameRoom GetGameRoomWithClientID(int id)
    {
        // checks through list of game room instances
        foreach(GameRoom gr in gameRooms)
        {
            // check if ID comes from either of the players
            if(gr.playerID1 == id || gr.playerID2 == id)
            {
                // returns the gameroom instance they are in
                return gr;
            }
            // if ID belongs to none of the players
            else
            {
                // check ID of all observers in the room
                foreach(int observer in gr.observers)
                {
                    // if observer in the room has same ID
                    if(observer == id)
                    {
                        // returns the gameroom instance they are in
                        return gr;
                    }
                }
            }
        }
        return null;
    }

    private void SendtoAllObservers(GameRoom whichGameRoom, string nameOfSender, int senderID, string message,
        int msgType = ServertoClientSignifiers.chatReceivedTypeServer)
    {
        foreach (int ID in whichGameRoom.observers)
        {
            // sends to all observers except id of sender (incase sender is an observer)
            if (ID != senderID)
            {
                string msgList = ServertoClientSignifiers.chatBoxMessageReceive + "," + nameOfSender + ": " + message 
                    + "," + msgType;
                SendMessageToClient(msgList, ID);
            }
        }
    }
}

public class PlayerAccount
{
    public string name, password;
    public int connectionID;
    public PlayerAccount(string Name, string Password)
    {
        name = Name;
        password = Password;
    }
}

[System.Serializable]
public struct connectionIDprofile
{
    public int connectionID;
    public string profileName;

    public connectionIDprofile(int id, string ProfileName)
    {
        profileName = ProfileName;
        connectionID = id;
    }
}

public class GameRoom
{
    public int playerID1, playerID2;
    public List<int> observers;

    public GameRoom(int PlayerID1, int PlayerID2)
    {
        playerID1 = PlayerID1;
        playerID2 = PlayerID2;
        observers = new List<int>();
    }
}

public static class ClientToServerSignifiers
{
    public const int CreateAccount = 1;
    public const int LoginAccount = 2;
    public const int JoinQueueForGameRoom = 3;
    public const int TicTacToeShapeSelectPlay = 4;
    public const int ChatBoxMessageSend = 5;
    public const int JoinAsObserver = 6;
}

static public class ServertoClientSignifiers
{
    public const int CreateAccountFail = 1;
    public const int LoginAccountFail = 2;

    public const int CreateAccountSuccess = 3;
    public const int LoginAccountSuccess = 4;

    public const int OpponentPlay = 5;
    public const int GameStart = 6;
    public const int chatBoxMessageReceive = 7;

    public const int ObserverJoined = 8;
    public const int chatReceivedTypePlayer = 9;
    public const int chatReceivedTypeObserver = 10;
    public const int chatReceivedTypeServer = 11;
}
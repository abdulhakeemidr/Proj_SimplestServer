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

    const int PlayerAccountRecord = 1;

    string PlayerAccountFilePath;

    int playerWaitingForMatchID = -1;

    LinkedList<GameRoom> gameRooms;

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
                SavePlayerAccounts();
                Debug.Log("Created Account");
                // save list to HD
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
        foreach(GameRoom gr in gameRooms)
        {
            if(gr.playerID1 == id || gr.playerID2 == id)
            {
                return gr;
            }
        }
        return null;
    }
}

public class PlayerAccount
{
    public string name, password;

    public PlayerAccount(string Name, string Password)
    {
        name = Name;
        password = Password;
    }
}

public class GameRoom
{
    public int playerID1, playerID2;

    public GameRoom(int PlayerID1, int PlayerID2)
    {
        playerID1 = PlayerID1;
        playerID2 = PlayerID2;
    }
}

public static class ClientToServerSignifiers
{
    public const int CreateAccount = 1;
    public const int LoginAccount = 2;
    public const int JoinQueueForGameRoom = 3;
    public const int TicTacToeShapeSelectPlay = 4;
}

static public class ServertoClientSignifiers
{
    public const int CreateAccountFail = 1;
    public const int LoginAccountFail = 2;

    public const int CreateAccountSuccess = 3;
    public const int LoginAccountSuccess = 4;

    public const int OpponentPlay = 5;
    public const int GameStart = 6;
}
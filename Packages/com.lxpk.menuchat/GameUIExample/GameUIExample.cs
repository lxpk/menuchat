using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CardChat.UI;

public class GameUIExample : MonoBehaviour
{
    public bool sendEvents = true;
    public MenuChatUIUXml menuChat;
    public GameObject mainMenuGO;
    public GameObject playMenuGO;

    // Start is called before the first frame update
    void Start()
    {
        if (menuChat == null)
        {
            Debug.Log("menuChat not set");
            //menuChat = MenuChatUIUXml.Instance;
        }    
    }

    public void GoMainMenu()
    {
        mainMenuGO.SetActive(true);
        playMenuGO.SetActive(false);
        if (sendEvents) menuChat.SendMessage("UIScreenChange");
    }

    public void GoPlayMenu()
    {
        mainMenuGO.SetActive(false);
        playMenuGO.SetActive(true);
        if (sendEvents) menuChat.SendMessage("UIScreenChange");
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}

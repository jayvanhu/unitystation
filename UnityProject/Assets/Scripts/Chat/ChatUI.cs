﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System;

public class ChatUI : MonoBehaviour
{
	public static ChatUI Instance;
	public GameObject chatInputWindow;
	public Transform content;
	public GameObject chatEntryPrefab;
	[SerializeField] private Text chatInputLabel;
	[SerializeField] private RectTransform channelPanel;
	[SerializeField] private GameObject channelToggleTemplate;
	[SerializeField] private GameObject background;
	[SerializeField] private GameObject uiObj;
	[SerializeField] private GameObject activeRadioChannelPanel;
	[SerializeField] private GameObject activeChannelTemplate;
	[SerializeField] private InputField InputFieldChat;
	[SerializeField] private Image toggleChatBubbleImage;
	[SerializeField] private Color toggleOffCol;
	[SerializeField] private Color toggleOnCol;
	[SerializeField] private Image scrollHandle;
	[SerializeField] private Image scrollBackground;
	private bool windowCoolDown = false;

	private ChatChannel selectedChannels;

	/// <summary>
	/// The currently selected chat channels. Prunes all unavailable ones on get.
	/// </summary>
	public ChatChannel SelectedChannels
	{
		get { return selectedChannels & GetAvailableChannels(); }
		set { selectedChannels = value; }
	}

	/// <summary>
	/// A map of channel names and their toggles for UI manipulation
	/// </summary>
	private Dictionary<ChatChannel, Toggle> ChannelToggles = new Dictionary<ChatChannel, Toggle>();

	/// <summary>
	/// A map of channel names and their active radio channel entry for UI manipulation
	/// </summary>
	private Dictionary<ChatChannel, GameObject> ActiveChannels = new Dictionary<ChatChannel, GameObject>();

	//All the current chat entries in the chat feed
	private List<ChatEntry> allEntries = new List<ChatEntry>();
	private int hiddenEntries = 0;
	private bool scrollBarInteract = false;
	public event Action<bool> scrollBarEvent;

	/// <summary>
	/// The main channels which shouldn't be active together.
	/// Local, Ghost and OOC.
	/// Order determines default selection priority so DON'T CHANGE THE ORDER!
	/// </summary>
	private static readonly List<ChatChannel> MainChannels = new List<ChatChannel>
	{
		ChatChannel.Local,
		ChatChannel.Ghost,
		ChatChannel.OOC
	};

	/// <summary>
	/// Radio channels which should also broadcast to local
	/// </summary>
	private static readonly List<ChatChannel> RadioChannels = new List<ChatChannel>
	{
		ChatChannel.Common,
		ChatChannel.Binary,
		ChatChannel.Supply,
		ChatChannel.CentComm,
		ChatChannel.Command,
		ChatChannel.Engineering,
		ChatChannel.Medical,
		ChatChannel.Science,
		ChatChannel.Security,
		ChatChannel.Service,
		ChatChannel.Syndicate
	};

	/// <summary>
	/// The last available set of channels. May be out of date.
	/// </summary>
	private ChatChannel availableChannelCache;

	/// <summary>
	/// Are the channel toggles on show?
	/// </summary>
	private bool showChannels = false;

	private void Awake()
	{
		if (Instance == null)
		{
			Instance = this;
			DontDestroyOnLoad(gameObject);
			uiObj.SetActive(true);
			InitPrefs();
		}
		else
		{
			Destroy(gameObject); //Kill the whole tree
		}
	}

	void InitPrefs()
	{
		if (!PlayerPrefs.HasKey(PlayerPrefKeys.ChatBubbleKey))
		{
			PlayerPrefs.SetInt(PlayerPrefKeys.ChatBubbleKey, 0);
			PlayerPrefs.Save();
		}

		if (PlayerPrefs.GetInt(PlayerPrefKeys.ChatBubbleKey) == 1)
		{
			toggleChatBubbleImage.color = toggleOnCol;
		}
	}

	public void Start()
	{
		// Create all the required channel toggles
		InitToggles();

		// Make sure the window and channel panel start disabled
		chatInputWindow.SetActive(false);
		channelPanel.gameObject.SetActive(false);
		EventManager.AddHandler(EVENT.UpdateChatChannels, OnUpdateChatChannels);
	}

	private void OnDestroy()
	{
		EventManager.RemoveHandler(EVENT.UpdateChatChannels, OnUpdateChatChannels);
	}

	private void Update()
	{
		// TODO add events to inventory slot changes to trigger channel refresh
		if (chatInputWindow.activeInHierarchy && !isChannelListUpToDate())
		{
			Logger.Log("Channel list is outdated!", Category.UI);
			RefreshChannelPanel();
		}

		if (KeyboardInputManager.IsEnterPressed() && !windowCoolDown)
		{
			if (UIManager.IsInputFocus)
			{
				if (!string.IsNullOrEmpty(InputFieldChat.text.Trim()))
				{
					PlayerSendChat();
				}

				CloseChatWindow();
			}
		}

		if (!chatInputWindow.activeInHierarchy) return;
		if (KeyboardInputManager.IsEscapePressed())
		{
			CloseChatWindow();
		}

		if (InputFieldChat.isFocused) return;
		if (KeyboardInputManager.IsMovementPressed() || KeyboardInputManager.IsEscapePressed())
		{
			CloseChatWindow();
		}
	}

	/// <summary>
	/// Only to be used via chat relay!
	/// </summary>
	public void AddChatEntry(string message)
	{
		//Check for chat entry dupes:
		if (allEntries.Count != 0)
		{
			if (message.Equals(allEntries[allEntries.Count - 1].text.text))
			{
				allEntries[allEntries.Count - 1].AddChatDuplication();
				return;
			}
		}

		GameObject entry = Instantiate(chatEntryPrefab, Vector3.zero, Quaternion.identity);
		var chatEntry = entry.GetComponent<ChatEntry>();
		chatEntry.text.text = message;
		entry.transform.SetParent(content, false);
		entry.transform.localScale = Vector3.one;
		allEntries.Add(chatEntry);
		hiddenEntries++;
		ReportEntryState(false);
	}

	public void ReportEntryState(bool isHidden, bool fromCoolDownFade = false)
	{
		if (isHidden)
		{
			if (hiddenEntries < 0) hiddenEntries = 0;
			hiddenEntries++;
			DetermineScrollBarState(fromCoolDownFade);
		}
		else
		{
			hiddenEntries--;
			DetermineScrollBarState(fromCoolDownFade);
		}
	}

	private void DetermineScrollBarState(bool coolDownFade)
	{
		if ((allEntries.Count - hiddenEntries) < 20)
		{
			float fadeTime = 0f;
			if (coolDownFade) fadeTime = 3f;
			scrollBackground.CrossFadeAlpha(0.01f, fadeTime, false);
			scrollHandle.CrossFadeAlpha(0.01f, fadeTime, false);
		}
		else
		{
			scrollBackground.CrossFadeAlpha(1f, 0f, false);
			scrollHandle.CrossFadeAlpha(1f, 0f, false);
		}
	}

	//This is an editor interface trigger event, do not delete
	public void OnScrollBarInteract()
	{
		scrollBarInteract = true;
		scrollBarEvent?.Invoke(scrollBarInteract);
	}

	//This is an editor interface trigger event, do not delete
	public void OnScrollBarInteractEnd()
	{
		scrollBarInteract = false;
		scrollBarEvent?.Invoke(scrollBarInteract);
	}

	private void OnUpdateChatChannels()
	{
		TrySelectDefaultChannel();
		RefreshChannelPanel();
	}

	public void OnClickSend()
	{
		if (!string.IsNullOrEmpty(InputFieldChat.text.Trim()))
		{
			SoundManager.Play("Click01");
			PlayerSendChat();
		}

		CloseChatWindow();
	}

	/// <summary>
	/// Toggles the Chat Icon / Chat Bubble preference
	/// </summary>
	public void OnClickToggleBubble()
	{
		SoundManager.Play("Click01");
		if (PlayerPrefs.GetInt(PlayerPrefKeys.ChatBubbleKey) == 1)
		{
			PlayerPrefs.SetInt(PlayerPrefKeys.ChatBubbleKey, 0);
			toggleChatBubbleImage.color = toggleOffCol;
		}
		else
		{
			PlayerPrefs.SetInt(PlayerPrefKeys.ChatBubbleKey, 1);
			toggleChatBubbleImage.color = toggleOnCol;
		}

		PlayerPrefs.Save();
		EventManager.Broadcast(EVENT.ToggleChatBubbles);
	}

	private void PlayerSendChat()
	{
		// Selected channels already masks all unavailable channels in it's get method
		PostToChatMessage.Send(InputFieldChat.text, SelectedChannels);
		InputFieldChat.text = "";
	}

	public void OnChatCancel()
	{
		SoundManager.Play("Click01");
		InputFieldChat.text = "";
		CloseChatWindow();
	}

	/// <summary>
	/// Opens the chat window to send messages
	/// </summary>
	/// <param name="newChannel">The chat channels to select when opening it</param>
	public void OpenChatWindow(ChatChannel newChannel = ChatChannel.None)
	{
		//Prevent input spam
		if (windowCoolDown) return;
		windowCoolDown = true;
		StartCoroutine(WindowCoolDown());

		// Can't open chat window while main menu open
		if (GUI_IngameMenu.Instance.mainIngameMenu.activeInHierarchy)
		{
			return;
		}

		var availChannels = GetAvailableChannels();

		// Change the selected channel if one is passed to the function and it's available
		if (newChannel != ChatChannel.None && (availChannels & newChannel) == newChannel)
		{
			EnableChannel(newChannel);
		}
		else if (SelectedChannels == ChatChannel.None)
		{
			// Make sure the player has at least one channel selected
			TrySelectDefaultChannel();
		}
		// Otherwise use the previously selected channels again

		EventManager.Broadcast(EVENT.ChatFocused);
		chatInputWindow.SetActive(true);
		background.SetActive(true);
		UIManager.IsInputFocus = true; // should work implicitly with InputFieldFocus
		EventSystem.current.SetSelectedGameObject(InputFieldChat.gameObject, null);
		InputFieldChat.OnPointerClick(new PointerEventData(EventSystem.current));
		RefreshChannelPanel();
	}

	public void CloseChatWindow()
	{
		windowCoolDown = true;
		StartCoroutine(WindowCoolDown());
		UIManager.IsInputFocus = false;
		chatInputWindow.SetActive(false);
		EventManager.Broadcast(EVENT.ChatUnfocused);
		background.SetActive(false);
	}

	IEnumerator WindowCoolDown()
	{
		yield return WaitFor.EndOfFrame;
		windowCoolDown = false;
	}

	/// <summary>
	/// Will update the toggles, active radio channels and channel text
	/// </summary>
	private void RefreshChannelPanel()
	{
		Logger.LogTrace("Refreshing channel panel!", Category.UI);
		Logger.Log("Selected channels: " + ListChannels(SelectedChannels), Category.UI);
		RefreshToggles();
		RefreshRadioChannelPanel();
		UpdateInputLabel();
	}

	public void Toggle_ChannelPanel()
	{
		showChannels = !showChannels;
		SoundManager.Play("Click01");
		if (showChannels)
		{
			channelPanel.gameObject.SetActive(true);
			RefreshToggles();
		}
		else
		{
			channelPanel.gameObject.SetActive(false);
		}
	}

	/// <summary>
	/// Try to select the most appropriate channel (Local, Ghost then OOC)
	/// </summary>
	private void TrySelectDefaultChannel()
	{
		var availChannels = GetAvailableChannels();

		// Relies on the order of the channels being Local, Ghost then OOC!
		foreach (ChatChannel channel in MainChannels)
		{
			// Check if channel is available
			if ((availChannels & channel) == channel)
			{
				EnableChannel(channel);
				return;
			}
		}
	}

	/// <summary>
	/// Turn a ChatChannel into a string of all channels within it
	/// </summary>
	/// <returns>Returns a string of all of the channel names</returns>
	private static string ListChannels(ChatChannel channels, string separator = ", ")
	{
		string listChannels = string.Join(separator, EncryptionKey.getChannelsByMask(channels));
		return listChannels == "" ? "None" : listChannels;
	}

	/// <summary>
	/// Creates a channel toggle for the channel, and adds it to the ChannelToggles dictionary
	/// </summary>
	private void CreateToggle(ChatChannel channel)
	{
		// Check a channel toggle doesn't already exist
		if (ChannelToggles.ContainsKey(channel))
		{
			Logger.LogWarning($"Channel toggle already exists for {channel}!", Category.UI);
			return;
		}

		Logger.Log($"Creating channel toggle for {channel}", Category.UI);
		// Create the toggle button
		GameObject channelToggleItem =
			Instantiate(channelToggleTemplate, channelToggleTemplate.transform.parent, false);
		var uiToggleScript = channelToggleItem.GetComponent<UIToggleChannel>();

		//Set the new UIToggleChannel object and
		// Add it to a list for easy access later
		ChannelToggles.Add(channel, uiToggleScript.SetToggle(channel));
	}

	/// <summary>
	/// Creates an active radio entry for the channel, and adds it to the ActiveChannels dictionary
	/// </summary>
	private void CreateActiveRadioEntry(ChatChannel channel)
	{
		Logger.Log($"Creating radio channel entry for {channel}", Category.UI);
		// Create the template object which is hidden in the list but deactivated
		GameObject radioEntry = Instantiate(activeChannelTemplate, activeChannelTemplate.transform.parent, false);

		// Setup the name and onClick function
		radioEntry.GetComponentInChildren<Text>().text = channel.ToString();
		radioEntry.GetComponentInChildren<Button>().onClick.AddListener(() =>
		{
			SoundManager.Play("Click01");
			DisableChannel(channel);
		});
		// Add it to a list for easy access later
		ActiveChannels.Add(channel, radioEntry);
	}

	/// <summary>
	/// Creates all the channel toggles
	/// </summary>
	private void InitToggles()
	{
		// Create toggles for all main and radio channels
		foreach (ChatChannel channel in MainChannels)
		{
			CreateToggle(channel);
		}

		foreach (ChatChannel channel in RadioChannels)
		{
			CreateToggle(channel);
		}
	}

	/// <summary>
	/// Will show all available channel toggles, and hide the rest
	/// </summary>
	private void RefreshToggles()
	{
		ChatChannel availChannels = GetAvailableChannels();

		foreach (var entry in ChannelToggles)
		{
			ChatChannel toggleChannel = entry.Key;
			GameObject toggle = entry.Value.gameObject;
			// If the channel is available activate it's toggle, otherwise disable it
			if ((availChannels & toggleChannel) == toggleChannel)
			{
				toggle.SetActive(true);
			}
			else
			{
				toggle.SetActive(false);
			}
		}
	}

	/// <summary>
	/// Will show the active radio channel panel if a radio channel is active, otherwise hide it
	/// </summary>
	private void RefreshRadioChannelPanel()
	{
		// Enable the radio panel if radio channels are active, otherwise hide it
		foreach (var radioChannel in RadioChannels)
		{
			// Check if the radioChannel is set in SelectedChannels
			if ((SelectedChannels & radioChannel) == radioChannel)
			{
				activeRadioChannelPanel.SetActive(true);
				return;
			}
		}

		activeRadioChannelPanel.SetActive(false);
	}

	public void Toggle_Channel(bool turnOn)
	{
		SoundManager.Play("Click01");
		GameObject curObject = EventSystem.current.currentSelectedGameObject;
		if (!curObject)
		{
			return;
		}

		UIToggleChannel source = curObject.GetComponent<UIToggleChannel>();
		if (!source)
		{
			return;
		}

		if (turnOn)
		{
			EnableChannel(source.channel);
		}
		else
		{
			DisableChannel(source.channel);
		}
	}

	private void TryDisableOOC()
	{
		// Disable OOC if it's on
		if (ChannelToggles.ContainsKey(ChatChannel.OOC) && ChannelToggles[ChatChannel.OOC].isOn)
		{
			SelectedChannels &= ~ChatChannel.OOC;
			ChannelToggles[ChatChannel.OOC].isOn = false;
		}
	}

	private void ClearTogglesExcept(ChatChannel channel)
	{
		foreach (KeyValuePair<ChatChannel, Toggle> chanToggle in ChannelToggles)
		{
			if (chanToggle.Key == channel)
			{
				continue;
			}

			chanToggle.Value.isOn = false;
		}
	}

	private void ClearToggles()
	{
		foreach (var entry in ChannelToggles)
		{
			// Disable the toggle
			entry.Value.isOn = false;
		}
	}

	private void ClearActiveRadioChannels()
	{
		Logger.Log("Clearing active radio channel panel", Category.UI);
		foreach (var channelEntry in ActiveChannels)
		{
			channelEntry.Value.SetActive(false);
		}

		activeRadioChannelPanel.SetActive(false);
	}

	/// <summary>
	/// Updates the label next to the chat input field
	/// </summary>
	private void UpdateInputLabel()
	{
		if ((SelectedChannels & ChatChannel.OOC) == ChatChannel.OOC)
		{
			chatInputLabel.text = "OOC:";
		}
		else if ((SelectedChannels & ChatChannel.Ghost) == ChatChannel.Ghost)
		{
			chatInputLabel.text = "Ghost:";
		}
		else
		{
			chatInputLabel.text = "Say:";
		}
	}

	/// <summary>
	/// Checks if the availableChannelCache is out of date and updates it if so
	/// </summary>
	private bool isChannelListUpToDate()
	{
		ChatChannel availableChannels = GetAvailableChannels();

		// See if available channels have changed
		if (availableChannelCache != availableChannels)
		{
			availableChannelCache = availableChannels;
			return false;
		}
		else
		{
			return true;
		}
	}

	/// <summary>
	/// Enable a channel and perform all special logic for it.
	/// Main channels disable all other channels, and radio channels enable local too
	/// </summary>
	private void EnableChannel(ChatChannel channel)
	{
		Logger.Log($"Enabling {channel}", Category.UI);

		if (ChannelToggles.ContainsKey(channel))
		{
			ChannelToggles[channel].isOn = true;
		}
		else
		{
			Logger.LogWarning($"Can't enable {channel} because it isn't in ChannelToggles!");
		}

		//Deselect all other channels in UI if it's a main channel
		if (MainChannels.Contains(channel))
		{
			ClearTogglesExcept(channel);
			ClearActiveRadioChannels();
			SelectedChannels = channel;
		}
		else
		{
			// Disable OOC and enable the channel
			TryDisableOOC();
			SelectedChannels |= channel;

			// Only enable local if it's a radio channel
			if (RadioChannels.Contains(channel))
			{
				// Activate local channel again
				SelectedChannels |= ChatChannel.Local;

				if (ChannelToggles.ContainsKey(channel))
				{
					ChannelToggles[ChatChannel.Local].isOn = true;
				}

				// Only add to active channel list if it's a radio channel
				if (!ActiveChannels.ContainsKey(channel))
				{
					CreateActiveRadioEntry(channel);
				}

				ActiveChannels[channel].SetActive(true);
				activeRadioChannelPanel.SetActive(true);
			}
		}

		UpdateInputLabel();
	}

	/// <summary>
	/// Disable a channel and perform all special logic for it.
	/// Main channels can't be disabled, and radio channels can hide the active radio channel panel
	/// </summary>
	private void DisableChannel(ChatChannel channel)
	{
		Logger.Log($"Disabling {channel}", Category.UI);

		// Special behaviour for main channels
		if (MainChannels.Contains(channel))
		{
			ClearToggles();
			ClearActiveRadioChannels();

			// Make sure toggle is still on so player can't disable them all
			ChannelToggles[channel].isOn = true;
			SelectedChannels = channel;
		}
		else
		{
			// Remove channel from SelectedChannels and disable toggle
			SelectedChannels &= ~channel;
			if (ChannelToggles.ContainsKey(channel))
			{
				ChannelToggles[channel].isOn = false;
			}

			if (RadioChannels.Contains(channel))
			{
				ActiveChannels[channel].SetActive(false);
				RefreshRadioChannelPanel();
			}
		}

		UpdateInputLabel();
	}

	private ChatChannel GetAvailableChannels()
	{
		if (PlayerManager.LocalPlayerScript == null)
		{
			return ChatChannel.OOC;
		}
		else
		{
			return PlayerManager.LocalPlayerScript.GetAvailableChannelsMask();
		}
	}
}
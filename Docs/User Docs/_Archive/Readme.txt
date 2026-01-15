DISCLAIMER: Use this at your own risk. It is very much in pre-release alpha, beta, charlie whatsit mode!!

RULES: Not for onward sharing without my consent. Feel free to make improvements and let me know

DEVELOPMENT: See last section for future mods and feel free to suggest any improvements. I don't have a user manual but most controls have tooltips. Otherwise my brain dump below will have to do...

-----------------------------------------

To use my dashes all you need to do is copy a few files from the shared folder I have given you access to.

Go to your main SimHub folder (somewhere like this - C:\Program Files (x86)\SimHub)

Drop in the DLL files:

RSC.iRacingExtraProperties.dll  [ESSENTIAL]
LaunchPlugin.dll [ESSENTIAL]
DahlDesign.dll   [You only need to install this if you intend to use the Lala-Dash]

Import the Dashes you want:

This can be done by double clicking the file or using the import feature from within SimHub

Lala-Dash: This is my dash which compliments your usual dash (I have mine behind my wheel on an old android phone and use Lovely Dasboard as an overlay on screen over the in car dash)
Message System Dash: This is my strategy and messages dash which is a secondary dash (I have it bottom right under my middle screen using an old android phone)			

Lovely Dashboard-Lala: 	This one will overwrite your current Lovely Dashboard (You must have Lovely Plugin Installed), I have removed the videos from the dash folder as they took up unecessary space.  

Lovely Dashboard TK Edition-Lala: This one will overwrite your current Lovely Dashboard (You must have Lovely Plugin Installed), I have left the videos in the dash folder as they give it its cool background.  

For both the Lovely dashes, you still get all the normal functionality of Lovely Dashboard but you get the "Lala Special Overlays". These are controlled as per instructions below.

You can convert any of the dashes to overlays to go on screen using SimHub DashStudio main page.

-----------------------------------------

Plugin setup:

All the plugins can be selected to show in the left menu on simhub. You only really need "Lala Plugin" shown. Once in Lala Plugin you will see several tabs. There is a lot of content and I will produce a user manual when the plugin is finished. For now you should do the following:

[DASH CONTROL]

DASH BINDINGS
1. Bind the Cancel Msg Button to something useful. Important because pressing it will suppress popups and messages. Mostly suppresses for a set time and resets.
2. Bind the Pit Screen Toggle. This will allow you to show the pit screens for my dashes at any time or cancel them when the activate automatically in the pits.
3. Other bindings don't matter, launch has a button on message dash and the Dash Modes are not in use yet

GLOBAL DASH FUNCTIONS
Most are self explanatory, the important ones are the 2 columns of toggles. These control what happens on the dashes and the overlay. Right now the overlay on the Lovely dashes is also controlled by the MAIN DASH controls (I will add a seperate column soon). Most toggles are self explanatory but...
1. For pit entry assist popup enable the Pit Limiter Screen
2. Show Verbose Messages - Uncheck to reduce level of alerts
3. Radio Messages is not in use yet
4. Show Traffic Alerts controls the info of faster cars in different class behind in Multi-class Races

USER VARIABLES
Use these to alter thresholds related to the popups
1. Rejoin Linger is how long you see alerts after getting back on the driving line, Rejoin clear speed does the same and which ever one happens first clears the popup.
2. Spin Yaw threshold. Increase this if you ever get nusence rejoin-spin alerts. 
3. Pit Entry Decel, this is also set per car but can be set here too for default value or a car when session is live. 15 is good for GTP and 13 is good for GT3. If you are getting pit speed too early, increase the value. It does not take account of surface type or any conditions. Values were found testing mid weight car on dry oval circuit.
4. Pit entry buffer is only for the brake messages and not the dynamic scale. Call it your reaction time. I welcome feedback if this is better applied to the point at which you should be on speed before the line. 
5. For pit entry, track markers for the entry and exit lines must be learnt. More on that later.

[LAUNCH SETTINGS]
1. These all control variables for launch screen and recording. They will set default or if a car is active, specific to that car. 
2. Telemetry toggle allow saving of launches in 2 CSV files which can be shown in the Analysis tab.
3. The logging toggles can stay off. They are for use during debugging only and use more CPU if active.

[POST LAUNCH ANALYSIS]
1. Here you can select files to display info and graphics of your launches and race starts. File recording can be a bit buggy.
2. The graph is great to see parameters against each other. If you compare 2 parameters with vastly different Y scale numbers, use the normalise data toggle to see them better

[FUEL]
This is the most powerful (and complex) part of the plugin and it relies heavily on the profiles section for data
It works in planning(profile) and live(telemetry) modes. 
1. Live Mode. The ultimate goal of live mode is to gather data but you can also work strategy directly from its output.
2. Planner Mode. This uses the stored car and track data to build strategy from the sliders. The calculated Strategy is very complex and tries to take everything possible into account such as being lapped (leader pace delta), how long you may have to keep driving after race clock zero (timed races), impact of pitstops, advice on fuel save to save stops.
3. Presets can be chosen for your regular races. Set these up in the PRESETS tab.
4. PitDrive-through Loss: This is calculated in several different ways. Best is by driving about 5 clean laps and then entering pits and driving through, follow this with a full out lap and additional lap and it will do a perfect calculation of your actual time lost in a race by pitting (also needed for pit exit race position prediction). If this is not seen before, it used the time between pit entry and exit lines or a 25s default. You only have to do it once per car/track and can lock it in when happy.
5. Other pit strategy: set tyre change time, GT3 is normally 22 seconds. If you intend to only take tyres every other stop, set it to 11 seconds for endurance races. Refuel time is calculated by learning the refuel rate for the car on first time refuel. It can be manually changed in the car profile. GT3 is around 2.6 litres/sec, GTP is closer to 2 litres/sec. With all these stored you get accurate stop estimates. Repair time is not exposed in telemetry, so that has to be added on top by the driver.
6. Fuel burn and average pace take several laps to stabalise and laps. There is rejection login for cold tyres (2 laps), bad offtracks, laps with pits, laps with black flags. Live snapshot shows the confidence level and you can set on the Dash control tab the level you want before info is displayed on the dashes. Eco and Push figures are also gaurded and should reject nonsence (most of the time). If you see anything weird, clear them in the profile section.
7. Max fuel override is there mainly for the planning mode. It will detect and lock for live sessions. For planner you can save the max tank size in the profiles tab.
8. If anything seems weird, move some value off and back or try the refresh calcs button.
9. If you like the data you see from the live session or adjustments you made you can save to the profile. It will also auto save on simhub exit if you have the values unlocked in Profile.

[PROFILES]
This is the memory store for cars and tracks and feeds the fuel tab, launch display and rejoin modules. So it is important to understand how it works!
1. Master profile is each car and then there are sub tabs:
	a. DASH car specific controls like seen on the main dash.
	b. LAUNCH car specific controls like seen on the main dash.
	c. FUEL some default car settings. Asjuster for fuel burn use predictions when wet detected. Generic pace delta to leader in races. 
	Refuel rate seen (I will add a lock for this later). Tank limit, when car live use button to fill it for you.
	d. TRACKS is the main store and is per track variant. 

2. TRACKS Sub Tab:
	a. PIT DATA, here you will see the recorded pit loss value and source (Drive Time Loss - DTL is the best one). Make sure to lock it when you have a good value. 
	If it is locked and it sees a vastly different value, you will get a message here. 
	Below is the track markers: These are learned because the other source is from iRacingExtraProperties which has not been updated in a while (exmple, Daytona line is now 40m earlier). 
	So, it is important on first drive in any car on the track to record them and lock when happy. 
	The simhub log will record any issues. This will be captured when you do the first proper pit stop or drive through.
	b. Dry and wet conditions data: Best Lap is info only, average laptimes and burns are used in calcs, so make sure the values are good and update them if conditions change lots. 
	Lock them to prevent live session overwriting or saving on simhub exit.
	d. Wet vs Dry is infornmational only.
	e. If in doubt, zero and relearn any of these.

[PRESETS]
Self explanatory and feeds the fuel tab for quick setting parameters.

----------------------

Data Storage:

Logs for launch and race summaries (partially working) are in the simhub Logs folder C:\Program Files (x86)\SimHub\Logs
Plugin settings are in JSONS you can edit directly if needed in the plugins data folder C:\Program Files (x86)\SimHub\PluginsData\Common\LalaPlugin

------------------------

Dashes:

both my dashes will respond to the global SimHub Next Page and Previous Page bindings. Next Page tends to change Screen and Previous Page does inset pages or widgets. You will get a feel for it. They all have touch screen areas as back up too.

Lala-Dash: It should auto detect the session type and change page automatically.

Pages: Far Left or right of screen touch to change page

Racing: Your Class Standings derrived Internal touch on the oponent ahead or behind will zoom on that oponent. Touch again to return. Lots of numbers, but green means good for you is a good rule.

AHD/BHD: On Track, any class, any lap derrived. Touch areas and zoom same as Racing page.

Timing (Qualy): Middle screen delta - Press for 4 delta view options. Messages and warnings related to the lap and wether you have time or fuel to complete it.

Practice: Like the Timing screen but shows inputs and some stats. White number at bottom of brake scale is the previous brake peak.

Pit Popup: Pit info and assists. This is partially finished and will be complete soon!


-----------------------

Future Developments:

*Instrumentation mode (practice-gated) – UI toggle to enable active test capture safely.

*Testing / instrumentation dash page – dedicated dash for decel (G-cell) capture and future test tools.

*Declutter mode – driver toggle to hide non-critical dash elements and suppress medium/low messages.

*Pit stop debrief log line – single concise SimHub log of actual pit execution vs assumptions (parked).

*Alerts overlay dash – standalone overlay for pit, rejoin, stall, limiter, and other attention-only alerts.

*Ahead/behind driver status enums (practice & quali) – simple OutLap / PushLap / SlowLap labels.

*Ahead/behind driver status enums (race) – Racing, FasterClass, SlowerClass, LappingYou, BeingLapped.

*Single unified eNumbers system with ordered ranges – low=quali only, mid=shared, high=race only, ordered to allow future multi-class open quali.




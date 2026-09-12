using UnityEngine;

// Uses the same immediate-mode UI as the prototype HUD; no UI package is required.
[RequireComponent(typeof(FloatingMap))]
public class TitleScreen : MonoBehaviour
{
    FloatingMap map;
    bool help;
    GUIStyle title, subtitle, heading, body, button, gameOverTitle;
    void Awake() { map = GetComponent<FloatingMap>(); }
    void Update()
    {
        if (map.HasStarted) return;
        if (help && Input.GetKeyDown(KeyCode.Escape)) help = false;
    }
    void PrepareStyles()
    {
        if (title != null) return;
        title = new GUIStyle(GUI.skin.label) { fontSize = 48, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        title.normal.textColor = Color.white;
        gameOverTitle = new GUIStyle(title) { fontSize = 76 };
        gameOverTitle.normal.textColor = new Color(1,.32f,.22f);
        subtitle = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
        subtitle.normal.textColor = new Color(.65f,.75f,.85f);
        heading = new GUIStyle(title) { fontSize = 28 };
        body = new GUIStyle(GUI.skin.label) { fontSize = 19, wordWrap = true, richText = true };
        body.normal.textColor = new Color(.88f,.92f,.97f);
        button = new GUIStyle(GUI.skin.button) { fontSize = 24, fontStyle = FontStyle.Bold };
    }
    void OnGUI()
    {
        if (!map || (map.HasStarted && !map.IsGameOver)) return;
        PrepareStyles();
        Matrix4x4 oldMatrix = GUI.matrix;
        Color oldColour = GUI.color;
        int oldDepth = GUI.depth;
        GUI.depth = -100;
        GUI.color = new Color(.025f,.035f,.065f,1);
        GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),Texture2D.whiteTexture);
        GUI.color = Color.white;
        float scale = Mathf.Min(Screen.width / 960f, Screen.height / 640f);
        GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width-960*scale)/2,(Screen.height-640*scale)/2,0),Quaternion.identity,new Vector3(scale,scale,1));
        if (map.IsGameOver)
        {
            GUI.Label(new Rect(60,65,840,55),FloatingMap.GameTitle,heading);
            GUI.Label(new Rect(60,170,840,110),"GAME OVER",gameOverTitle);
            GUI.Label(new Rect(120,290,720,38),"All three lives lost • Reached round " + map.RoundNumber,subtitle);
            if (GUI.Button(new Rect(300,380,360,68),"Play Again",button)) map.RestartGame(true);
            if (GUI.Button(new Rect(300,468,360,68),"Title Screen",button)) map.RestartGame(false);
        }
        else
        {
        GUI.Label(new Rect(60,35,840,80),FloatingMap.GameTitle,title);
        GUI.Label(new Rect(60,112,840,30),"A game of colours, timing and survival",subtitle);
        if (!help)
        {
            GUI.Label(new Rect(120,220,720,40),"Find your colour. Hold your ground.",heading);
            if (GUI.Button(new Rect(330,320,300,64),"Start",button)) map.BeginGame();
            if (GUI.Button(new Rect(330,404,300,64),"Help",button)) help = true;
        }
        else
        {
            GUI.Label(new Rect(80,162,800,48),"How to play",heading);
            GUI.Label(new Rect(140,223,680,260),
                "<b>Move:</b> WASD or arrow keys     <b>Jump:</b> Space\n\n" +
                "Match a tile to <b>your character’s colour</b> before the timer ends. Your white outline identifies you.\n\n" +
                "Black tiles and the edge platform disappear. Standing on a wrong colour launches you off!\n\n" +
                "You have <b>3 lives</b>. After a fall, respawn on the edge next round. Survivors stay on their tile.",body);
            GUI.Label(new Rect(140,490,680,50),"Lose all three lives and the game ends. Press R during play to return to this title screen.",body);
            if (GUI.Button(new Rect(330,560,300,56),"Back",button)) help = false;
        }
        }
        GUI.matrix = oldMatrix; GUI.color = oldColour; GUI.depth = oldDepth;
    }
}

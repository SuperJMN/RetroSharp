import RetroSharp.Portable2D;

static class Level
{
    const i16 Width = 40;
    const i16 StreamY = 10;
    const i16 Height = 2;
}

void Main()
{
    Video.Init();
    World.Load("assets/actors.tmj");
    Camera.Init(Level.Width, Level.StreamY, Level.Height);
    Sprite.Asset(actor_marker, "assets/actor.json");
    Animation.Clip(actor_walk, 0, 16);

    Actors.Pool(enemies, 2);
    Enemies.Def(Goomba, sprite: actor_marker, behavior: Walker, animation: actor_walk, speed: 1, hp: 1, hitboxWidth: 8, hitboxHeight: 8);
    Enemies.Def(Bat, sprite: actor_marker, behavior: Flyer, animation: actor_walk, speed: 1, hp: 1, hitboxWidth: 8, hitboxHeight: 8);
    Enemies.Def(Koopa, sprite: actor_marker, behavior: Patrol, animation: actor_walk, speed: 1, hp: 1, cooldown: 24, hitboxWidth: 8, hitboxHeight: 8);

    u8 cameraX = 0;

    while (true)
    {
        Video.WaitVBlank();
        Input.Poll();
        if (Input.IsDown(Button.Right) && cameraX < 160)
        {
            cameraX += 1;
        }
        else if (Input.IsDown(Button.Left) && cameraX > 0)
        {
            cameraX -= 1;
        }

        Camera.SetPosition(cameraX, 0);
        Camera.Apply();
        Actors.SpawnLayer(enemies, "assets/actors.tmj", "actors");
        enemies.Update();
        enemies.TouchTiles(0, 1);
        enemies.LandOnTiles(4, 12, 1);
        enemies.Draw();
    }
}

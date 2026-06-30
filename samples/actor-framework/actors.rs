static class World {
    const i16 Width = 40;
    const i16 StreamY = 10;
    const i16 Height = 2;
}

void main() {
    video.Init();
    world.Load("actors.tmj");
    camera.Init(World.Width, World.StreamY, World.Height);
    sprite.Asset(actor_marker, "actor.json");
    animation.Clip(actor_walk, 0, 16);

    actor.Pool(enemies, 2);
    enemy.Def(Goomba, sprite: actor_marker, behavior: Walker, animation: actor_walk, speed: 1, hp: 1, hitboxWidth: 8, hitboxHeight: 8);
    enemy.Def(Bat, sprite: actor_marker, behavior: Flyer, animation: actor_walk, speed: 1, hp: 1, hitboxWidth: 8, hitboxHeight: 8);
    enemy.Def(Koopa, sprite: actor_marker, behavior: Patrol, animation: actor_walk, speed: 1, hp: 1, cooldown: 24, hitboxWidth: 8, hitboxHeight: 8);

    u8 cameraX = 0;

    loop {
        video.WaitVBlank();
        input.Poll();
        if (button_down(Button.Right) && cameraX < 160) {
            cameraX += 1;
        }

        camera.SetPosition(cameraX, 0);
        actor.SpawnLayer(enemies, "actors.tmj", "actors");
        enemies.Update();
        enemies.TouchTiles(0, 1);
        enemies.LandOnTiles(4, 12, 1);
        camera.Apply();
        enemies.Draw();
    }
}

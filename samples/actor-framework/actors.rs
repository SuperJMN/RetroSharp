enum World {
    Width = 40,
    StreamY = 10,
    Height = 2
}

void main() {
    video.Init();
    world.Load("actors.tmj");
    camera.Init(World.Width, World.StreamY, World.Height);
    sprite.Asset(actor_marker, "actor.json");
    animation.Clip(actor_walk, 0, 16);

    actor.Pool(enemies, 3);
    enemy.Def(Goomba, sprite: actor_marker, behavior: Walker, animation: actor_walk, speed: 1, hp: 1, hitboxWidth: 8, hitboxHeight: 8);
    enemy.Def(Bat, sprite: actor_marker, behavior: Flyer, animation: actor_walk, speed: 1, hp: 1, hitboxWidth: 8, hitboxHeight: 8);
    enemy.Def(Koopa, sprite: actor_marker, behavior: Patrol, animation: actor_walk, speed: 1, hp: 1, cooldown: 24, hitboxWidth: 8, hitboxHeight: 8);

    actor.SpawnLayer(enemies, "actors.tmj", "actors");

    u8 cameraX = 0;

    loop {
        video.WaitVBlank();
        input.Poll();
        if (button_down(right) && cameraX < 160) {
            cameraX += 1;
        }

        camera.SetPosition(cameraX, 0);
        enemies.Update();
        camera.Apply();
        enemies.Draw();
    }
}

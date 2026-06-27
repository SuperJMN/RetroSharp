enum World {
    Width = 16,
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

    actor.SpawnWindow(enemies, "actors.tmj", "actors", 0, 160);

    loop {
        video.WaitVBlank();
        enemies.Update();
        enemies.TouchTiles(72, 0, 1);
        enemies.LandOnTiles(72, 4, 12, 1);
        enemies.Draw();
        camera.Apply();
    }
}

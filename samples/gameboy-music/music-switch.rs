void main() {
    video.Init();
    music.Asset(terminate, "music/terminate.gbapu");
    music.Asset(blue_ocean, "music/blue_ocean_remix.uge");
    audio.Init();
    music.Play(terminate);

    u8 onBlueOcean = 0;
    loop {
        video.WaitVBlank();
        input.Poll();
        audio.Update();
        if (button_just_pressed(Button.Start) != 0) {
            if (onBlueOcean == 0) {
                music.Play(blue_ocean);
                onBlueOcean = 1;
            } else {
                music.Play(terminate);
                onBlueOcean = 0;
            }
        }
    }
}

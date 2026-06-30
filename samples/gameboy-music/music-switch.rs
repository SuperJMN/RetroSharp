void main() {
    video.Init();
    music.Asset(terminate, "music/terminate.gbapu");
    music.Asset(blue_ocean, "music/blue_ocean_remix.uge");
    audio.Init();
    music.Play(terminate);

    bool onBlueOcean = false;
    loop {
        video.WaitVBlank();
        input.Poll();
        audio.Update();
        if (button_just_pressed(Button.Start) != 0) {
            if (!onBlueOcean) {
                music.Play(blue_ocean);
                onBlueOcean = true;
            } else {
                music.Play(terminate);
                onBlueOcean = false;
            }
        }
    }
}

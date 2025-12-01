using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace MusicTimeline
{
    [RequireComponent(typeof( AudioSource ))]
    [RequireComponent( typeof( PlayableDirector) )]
    [ExecuteInEditMode]
    public class MusicEngine : MonoBehaviour
    {
        [SerializeField]
        private AudioSource audioSource;
        [SerializeField]
        private PlayableDirector playableDirector;

        [Header("MusicEngine Event")]
        public UnityEvent OnBar;
        public UnityEvent OnBeat;
        public UnityEvent OnUnit;

        [NonSerialized]
        public UnityEvent<Section> OnMusicEngineUpdate;

        private MusicClip musicClip;

        public int ChangeSectionIndexCue {
            get; set;
        }

        public AudioSource AudioSource {
            get {
                return audioSource;
            }
        }

        public PlayableDirector PlayableDirector {
            get {
                return playableDirector;
            }
        }

        private void Start()
        {
            audioSource = GetComponent<AudioSource>();

            ChangeSectionIndexCue = -1;
        }



        public void MusicEngineUpdate( Section currentSection )
        {
            OnMusicEngineUpdate?.Invoke( currentSection );
        }

        public void ChangeSection( string sectionName )
        {
            for( int i = 0; i < musicClip.sections.Count; i++ )
            {
                if (musicClip.sections[i].Name == sectionName)
                {
                    ChangeSectionIndexCue = i;
                    break;
                }
            }
        }

        public void ChangeSection( int index )
        {
            ChangeSectionIndexCue = index;
        }

    }

}